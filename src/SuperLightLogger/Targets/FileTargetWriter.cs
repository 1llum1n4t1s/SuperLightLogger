using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SuperLightLogger
{
    /// <summary>
    /// 同期書き込みのファイルターゲット実装本体。
    /// パステンプレート展開・日付ロール・サイズアーカイブ・古いファイルの掃除をすべて担当する。
    /// </summary>
    internal sealed class FileTargetWriter : IFileTargetWriter
    {
        private readonly FileTargetOptions _options;
        private readonly LayoutRenderer _layout;
        private readonly LayoutRenderer _filePath;
        private readonly LayoutRenderer? _archivePathTemplate;
        private readonly LayoutRenderer? _header;
        private readonly LayoutRenderer? _footer;
        private readonly object _lock = new object();

        private string? _currentFilePath;
        private FileStream? _stream;
        private long _currentSize;
        private DateTime _currentBoundary;
        private bool _disposed;

        // エラー無限スパム対策: 各カテゴリ1回だけ Console.Error に出す
        private bool _writeErrorEmitted;
        private bool _archiveErrorEmitted;

        // 書込みごとに new StringBuilder() / byte[] しないよう、lock 下で再利用するバッファ群。
        // _sb はパステンプレート展開と行レンダリングの両方で使い回す (使う直前に必ず Clear)。
        // _charBuffer / _byteBuffer は StringBuilder → bytes エンコード時の中間バッファ。
        // 容量は拡張のみ行い縮小はしない (これまでに記録された最長行 + エンコーディング最悪率で上限が決まる)。
        private readonly StringBuilder _sb = new StringBuilder(512);
        private char[] _charBuffer = new char[512];
        private byte[] _byteBuffer = new byte[1024];

        public FileTargetWriter(FileTargetOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _layout = new LayoutRenderer(options.Layout);
            _filePath = new LayoutRenderer(options.FileName);
            _archivePathTemplate = options.ArchiveFileName != null ? new LayoutRenderer(options.ArchiveFileName) : null;
            _header = !string.IsNullOrEmpty(options.Header) ? new LayoutRenderer(options.Header!) : null;
            _footer = !string.IsNullOrEmpty(options.Footer) ? new LayoutRenderer(options.Footer!) : null;
        }

        public void Write(in LogEvent ev)
        {
            // _disposed のチェックは lock 内で行う。
            // 外側で見ると Dispose 割り込みで _stream が null になる TOCTOU の窓がある。
            lock (_lock)
            {
                if (_disposed) return;
                try
                {
                    WriteCore(in ev);
                }
                catch (Exception ex)
                {
                    if (!_writeErrorEmitted)
                    {
                        _writeErrorEmitted = true;
                        Console.Error.WriteLine($"[SuperLightLogger.FileTarget] 書込みエラー: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }

        private void WriteCore(in LogEvent ev)
        {
            // ── パステンプレートを再利用 _sb に展開 ──
            // パスが前回と char 単位で一致するなら _currentFilePath を流用して string アロケーション 0 にする。
            // (NLog 互換テンプレートは ${shortdate} 程度なら 1 日中同じ結果なので、
            //  日次/時間粒度でも render-and-compare で正しく無アロケーションにできる)
            // FileName テンプレートは実 ev で描画する。${logger}/${level} 等を含む場合は
            // ロガー/レベル別ファイルが生成される (NLog 互換)。${message} 等の毎回変わる
            // フィールドを入れるとファイル数爆発するが、それは利用者責任。
            _sb.Clear();
            _filePath.Render(in ev, _sb);

            // _sb が複数 chunk に成長していると indexer は O(chunks) なので、
            // 比較も new string も毎回 chunk リンクリストを歩くことになる。
            // ここで一度だけ _charBuffer に CopyTo して、以降は flat な char[] 上で扱う。
            int pathLen = _sb.Length;
            if (_charBuffer.Length < pathLen)
                _charBuffer = new char[Math.Max(pathLen, _charBuffer.Length * 2)];
            _sb.CopyTo(0, _charBuffer, 0, pathLen);

            // _currentFilePath は CloseStream では消さず、KeepFileOpen=false 経路でも
            // 「直前にどのファイルへ書いていたか」を保持し続けるようにしている。
            // (時間境界アーカイブを動かすために必要 — _stream の開閉と独立させる)
            bool pathUnchanged = CharBufferEquals(pathLen, _currentFilePath);
            string newPath;
            if (pathUnchanged)
            {
                // パス未変更: 既存 string をそのまま再利用
                newPath = _currentFilePath!;
            }
            else
            {
                // パス変更検出。CloseStream は footer 描画で _sb / _charBuffer を再利用するため、
                // 先に _charBuffer から string を生成してから CloseStream を呼ぶこと。
                // 順番を逆にすると newPath が footer 内容で上書きされる。
                // (ArchiveCurrent → CloseStream 経路でも同じ罠があるので注意)
                newPath = new string(_charBuffer, 0, pathLen);
                // KeepFileOpen=false 経路では _stream は前回の Write 直後に閉じられているため、
                // そのまま CloseStream を呼んでも footer が書き出されない。
                // footer が設定されていれば古いアクティブファイルを append モードで再オープンしておく。
                ReopenForFooter();
                if (_stream != null)
                {
                    CloseStream(writeFooter: true);
                }

                // 動的 FileName テンプレート (例: app_${shortdate}.log) は path 変更自体が
                // 日次ロールを兼ねるため、ここで古い期間のファイルを MaxArchiveFiles で掃除する。
                // ArchiveFileName が設定されている場合は ArchiveCurrent 経由で別名にコピーする仕事なので
                // ここでは触らない (path 変更 = ただの自然な切替)。
                if (_options.MaxArchiveFiles > 0
                    && _archivePathTemplate == null
                    && TemplateHasDynamicTokens(_filePath.Template))
                {
                    try { CleanupOldArchives(newPath, ev.Timestamp); } catch { /* ignored */ }
                }
            }

            // ArchiveEvery による時間境界アーカイブ。
            // _stream の開閉と無関係に判定する (KeepFileOpen=false でも日次/週次ロールが効くように)。
            // ただしパス自体が変わった場合はパス変更が rotation の役割を兼ねるのでスキップする
            // (例: FileName に ${shortdate} を含み、かつ ArchiveEvery=Day という冗長設定)。
            if (pathUnchanged
                && _options.ArchiveEvery != ArchiveEvery.None
                && _currentFilePath != null
                && _currentBoundary != DateTime.MinValue)
            {
                var newBoundary = ComputeBoundary(ev.Timestamp, _options.ArchiveEvery);
                if (newBoundary != _currentBoundary)
                {
                    // 閉じる方の period (≒ 既存ファイルの中身が属する時間帯) で
                    // アーカイブ名を付けないと Date / DateAndSequence で「翌日付」になってしまう。
                    // newBoundary は新しい period の開始時刻なので、その 1 tick 前 = 旧 period の最終瞬間。
                    // ArchiveFileName に ${logger} などが入るケースを壊さないよう、
                    // Timestamp だけ置き換えた合成 LogEvent を渡す (他フィールドは現イベントを流用)。
                    var closingPeriodStamp = newBoundary.AddTicks(-1);
                    var closingEv = new LogEvent(
                        closingPeriodStamp,
                        ev.Level,
                        ev.Logger,
                        ev.Message,
                        ev.Exception,
                        ev.ThreadId,
                        ev.ThreadName);
                    ArchiveCurrent(in closingEv);
                }
            }

            EnsureOpen(newPath, ev.Timestamp);

            // ── 1行のレンダリングを再利用 _sb に行う ──
            _sb.Clear();
            bool isFreshFile = _currentSize == 0;
            if (isFreshFile && _header != null)
            {
                _header.Render(in ev, _sb);
                _sb.Append(_options.LineEnding);
            }
            _layout.Render(in ev, _sb);
            _sb.Append(_options.LineEnding);

            // 中間 string / byte[] を確保せずに直接エンコード→書込
            int written = FlushSbToStream();
            _currentSize += written;

            // KeepFileOpen=false の場合は直後に CloseStream → Dispose されて
            // FileStream 側で flush されるので、ここでの AutoFlush は冗長。
            if (_options.AutoFlush && _options.KeepFileOpen)
                _stream!.Flush();

            // サイズアーカイブ
            if (_options.ArchiveAboveSize > 0 && _currentSize >= _options.ArchiveAboveSize)
            {
                ArchiveCurrent(in ev);
            }

            if (!_options.KeepFileOpen)
            {
                CloseStream(writeFooter: false);
            }
        }

        public void Flush()
        {
            // _disposed のチェックは Write と同様に lock 内で行う。
            lock (_lock)
            {
                if (_disposed) return;
                try { _stream?.Flush(); } catch { /* ignored */ }
            }
        }

        // ───────── ファイル開閉 ─────────

        private void EnsureOpen(string path, DateTime now)
        {
            // ReplaceFileContentsOnEachWrite=true は「書込み毎に内容を完全置換」する仕様。
            // 既存ストリームを使い回すと FileMode.Create が初回しか効かず以降は append になるため、
            // たとえ同一パスでも一旦閉じて開き直す (footer は不要 — 上書きされる)。
            if (_stream != null
                && !_options.ReplaceFileContentsOnEachWrite
                && string.Equals(_currentFilePath, path, StringComparison.Ordinal))
                return;

            if (_stream != null)
            {
                try { _stream.Flush(); } catch { /* ignored */ }
                try { _stream.Dispose(); } catch { /* ignored */ }
                _stream = null;
                _currentSize = 0;
            }

            if (_options.CreateDirectories)
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir!);
                }
            }

            var mode = _options.ReplaceFileContentsOnEachWrite ? FileMode.Create : FileMode.Append;
            var share = _options.ConcurrentWrites ? FileShare.ReadWrite : FileShare.Read;
            _stream = new FileStream(path, mode, FileAccess.Write, share, bufferSize: 4096, FileOptions.SequentialScan);
            _currentFilePath = path;
            _currentSize = _stream.Length;
            _currentBoundary = ComputeBoundary(now, _options.ArchiveEvery);
        }

        private void CloseStream(bool writeFooter)
        {
            if (_stream == null) return;
            try
            {
                if (writeFooter && _footer != null && _stream.CanWrite)
                {
                    // _sb / _charBuffer は WriteCore とも共用。呼び出し側 (WriteCore のパス変更分岐や
                    // ArchiveCurrent) が必要な情報を string 化済みである前提でここから先は自由に Clear して使える。
                    _sb.Clear();
                    var ev = LogEvent.ForPath(DateTime.Now);
                    _footer.Render(in ev, _sb);
                    _sb.Append(_options.LineEnding);
                    FlushSbToStream();
                }
            }
            catch { /* ignored */ }

            try { _stream.Flush(); } catch { /* ignored */ }
            try { _stream.Dispose(); } catch { /* ignored */ }
            _stream = null;
            _currentSize = 0;
            // _currentFilePath / _currentBoundary は意図的に残す。
            // KeepFileOpen=false 経路で次の Write が来たときに「同じパスへの再オープンか」
            // 「時間境界を跨いだか」を判断するためにこの 2 つを永続させる必要がある。
            // (実際にファイルが消える ArchiveCurrent 経由では下流の EnsureOpen が上書きする)
        }

        // ───────── アーカイブ ─────────

        private void ArchiveCurrent(in LogEvent ev)
        {
            if (_currentFilePath == null) return;
            string activePath = _currentFilePath;

            // KeepFileOpen=false 経路では _stream は前回の Write 直後に閉じられているため、
            // ここで何もせず CloseStream を呼ぶと footer が書き出されない。
            // footer を出力する設定がある場合のみ append モードで再オープンしておき、
            // CloseStream の通常パスから footer を流す。
            ReopenForFooter();

            // フッターを書いて閉じる
            CloseStream(writeFooter: true);

            try
            {
                string archivePath = ResolveArchivePath(activePath, in ev);

                if (_options.CreateDirectories)
                {
                    var dir = Path.GetDirectoryName(archivePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir!);
                    }
                }

                if (System.IO.File.Exists(activePath))
                {
                    if (System.IO.File.Exists(archivePath))
                    {
                        try { System.IO.File.Delete(archivePath); } catch { /* ignored */ }
                    }
                    System.IO.File.Move(activePath, archivePath);
                }

                CleanupOldArchives(activePath, ev.Timestamp);
            }
            catch (Exception ex)
            {
                if (!_archiveErrorEmitted)
                {
                    _archiveErrorEmitted = true;
                    Console.Error.WriteLine($"[SuperLightLogger.FileTarget] アーカイブエラー: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// アクティブファイルパスとアーカイブ対象イベントからアーカイブパスを決定する。
        /// ArchiveFileName テンプレート内の <c>${logger}</c> などを正しく展開するため、
        /// タイムスタンプだけでなく実イベント全体を受け取る。
        /// </summary>
        private string ResolveArchivePath(string activePath, in LogEvent ev)
        {
            string dir = Path.GetDirectoryName(activePath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(activePath);
            string ext = Path.GetExtension(activePath);
            DateTime now = ev.Timestamp;

            switch (_options.ArchiveNumbering)
            {
                case ArchiveNumbering.Rolling:
                    return ResolveRollingArchive(dir, baseName, ext, in ev);

                case ArchiveNumbering.Date:
                    {
                        string datePart = now.ToString(_options.ArchiveDateFormat, CultureInfo.InvariantCulture);
                        if (_archivePathTemplate != null)
                        {
                            // ユーザーが ArchiveFileName に {#} を入れていた場合は、
                            // Sequence と同様に既存ファイルを走査して空き番号を探す。
                            // (ハードコード 0 だと毎回上書きになり既存アーカイブを破壊する)
                            string rendered = _archivePathTemplate.Render(in ev);
                            int n = FindNextSequenceFromTemplate(rendered);
                            return SubstituteSequencePlaceholder(rendered, n);
                        }
                        string candidate = Path.Combine(dir, $"{baseName}.{datePart}{ext}");
                        // 既存があれば DateAndSequence に倒す
                        if (System.IO.File.Exists(candidate))
                        {
                            int n = FindNextSequenceForPattern(dir, $"{baseName}.{datePart}.*{ext}", $"{baseName}.{datePart}.");
                            return Path.Combine(dir, $"{baseName}.{datePart}.{n}{ext}");
                        }
                        return candidate;
                    }

                case ArchiveNumbering.DateAndSequence:
                    {
                        string datePart = now.ToString(_options.ArchiveDateFormat, CultureInfo.InvariantCulture);
                        if (_archivePathTemplate != null)
                        {
                            string rendered = _archivePathTemplate.Render(in ev);
                            // テンプレに {#} が入っている前提
                            int n = FindNextSequenceFromTemplate(rendered);
                            return SubstituteSequencePlaceholder(rendered, n);
                        }
                        int seq = FindNextSequenceForPattern(dir, $"{baseName}.{datePart}.*{ext}", $"{baseName}.{datePart}.");
                        return Path.Combine(dir, $"{baseName}.{datePart}.{seq}{ext}");
                    }

                case ArchiveNumbering.Sequence:
                default:
                    {
                        if (_archivePathTemplate != null)
                        {
                            string rendered = _archivePathTemplate.Render(in ev);
                            int n = FindNextSequenceFromTemplate(rendered);
                            return SubstituteSequencePlaceholder(rendered, n);
                        }
                        int seq = FindNextSequenceForPattern(dir, $"{baseName}.*{ext}", $"{baseName}.");
                        return Path.Combine(dir, $"{baseName}.{seq}{ext}");
                    }
            }
        }

        private string ResolveRollingArchive(string dir, string baseName, string ext, in LogEvent ev)
        {
            // ArchiveFileName が設定されていればそのテンプレートを実イベントで展開し、
            // {#} を回転インデックスで差し替える。未設定時は従来の "baseName.{i}.ext" を使う。
            // (Rolling 用のシーケンス差し替えはテンプレート/非テンプレート両系で同一の形になる)
            string? renderedTemplate = _archivePathTemplate?.Render(in ev);

            string TopPath(int i)
            {
                if (renderedTemplate != null)
                {
                    return SubstituteSequencePlaceholder(renderedTemplate, i);
                }
                return Path.Combine(dir, $"{baseName}.{i}{ext}");
            }

            if (_options.MaxArchiveFiles > 0)
            {
                int max = _options.MaxArchiveFiles;
                try
                {
                    // File.Delete は存在しないファイルに対しても例外を投げないので、
                    // Exists 事前チェック (TOCTOU の窓 + 余分な syscall) は省略する。
                    System.IO.File.Delete(TopPath(max - 1));
                    for (int i = max - 2; i >= 0; i--)
                    {
                        var src = TopPath(i);
                        var dst = TopPath(i + 1);
                        try
                        {
                            System.IO.File.Delete(dst);
                            System.IO.File.Move(src, dst);
                        }
                        catch (FileNotFoundException) { /* src が無いだけ */ }
                        catch (DirectoryNotFoundException) { /* 同上 */ }
                    }
                }
                catch { /* ignored */ }
            }
            else
            {
                // MaxArchiveFiles=0 は無制限保持 (FileTargetOptions の仕様)。
                // 削除なしで既存の最大インデックスから降順にシフトする。
                try
                {
                    int highest = -1;
                    if (renderedTemplate != null)
                    {
                        // テンプレート経由: FindNextSequenceFromTemplate が max+1 を返すので -1 する。
                        highest = FindNextSequenceFromTemplate(renderedTemplate) - 1;
                    }
                    else
                    {
                        string searchDir = string.IsNullOrEmpty(dir) ? "." : dir;
                        if (Directory.Exists(searchDir))
                        {
                            string prefix = baseName + ".";
                            foreach (var f in Directory.GetFiles(searchDir, $"{baseName}.*{ext}"))
                            {
                                string fileName = Path.GetFileName(f);
                                if (!fileName.StartsWith(prefix, StringComparison.Ordinal)) continue;
                                if (ext.Length > 0 && !fileName.EndsWith(ext, StringComparison.Ordinal)) continue;
                                string mid = fileName.Substring(prefix.Length, fileName.Length - prefix.Length - ext.Length);
                                if (int.TryParse(mid, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                                    && n > highest && n <= MaxReasonableSequence)
                                    highest = n;
                            }
                        }
                    }
                    for (int i = highest; i >= 0; i--)
                    {
                        try { System.IO.File.Move(TopPath(i), TopPath(i + 1)); }
                        catch (FileNotFoundException) { /* src が無いだけ */ }
                        catch (DirectoryNotFoundException) { /* 同上 */ }
                    }
                }
                catch { /* ignored */ }
            }
            return TopPath(0);
        }

        // 異種ファイル名 (例: 日付フォーマットの 20260414) を巨大な連番として
        // 誤認しないためのガード閾値。NLog 互換でも実用上 99999 を超える連番は無い。
        private const int MaxReasonableSequence = 99999;

        private static int FindNextSequenceForPattern(string dir, string searchPattern, string baseNameWithDot)
        {
            int max = 0;
            if (string.IsNullOrEmpty(dir)) dir = ".";
            if (!Directory.Exists(dir)) return 1;
            foreach (var f in Directory.GetFiles(dir, searchPattern))
            {
                string fileName = Path.GetFileName(f);
                if (!fileName.StartsWith(baseNameWithDot, StringComparison.Ordinal)) continue;
                string rest = fileName.Substring(baseNameWithDot.Length);
                int dotIdx = rest.IndexOf('.');
                string numPart = dotIdx >= 0 ? rest.Substring(0, dotIdx) : rest;
                if (int.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                    && n > max && n <= MaxReasonableSequence)
                    max = n;
            }
            return max + 1;
        }

        private static int FindNextSequenceFromTemplate(string renderedTemplate)
        {
            int phPos = FindSequencePlaceholder(renderedTemplate, out int phLen);
            if (phPos < 0) return 1;

            string prefix = renderedTemplate.Substring(0, phPos);
            string suffix = renderedTemplate.Substring(phPos + phLen);
            string dir = Path.GetDirectoryName(renderedTemplate) ?? string.Empty;
            if (string.IsNullOrEmpty(dir)) dir = ".";
            if (!Directory.Exists(dir)) return 1;

            string prefixFile = Path.GetFileName(prefix);
            string searchPattern = $"{prefixFile}*{suffix}";
            int max = 0;
            foreach (var f in Directory.GetFiles(dir, searchPattern))
            {
                string fileName = Path.GetFileName(f);
                if (!fileName.StartsWith(prefixFile, StringComparison.Ordinal)) continue;
                if (!fileName.EndsWith(suffix, StringComparison.Ordinal)) continue;
                string middle = fileName.Substring(prefixFile.Length, fileName.Length - prefixFile.Length - suffix.Length);
                if (int.TryParse(middle, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                    && n > max && n <= MaxReasonableSequence)
                    max = n;
            }
            return max + 1;
        }

        private static string SubstituteSequencePlaceholder(string template, int number)
        {
            int pos = FindSequencePlaceholder(template, out int len);
            if (pos < 0) return template;
            return template.Substring(0, pos) + number.ToString(CultureInfo.InvariantCulture) + template.Substring(pos + len);
        }

        /// <summary>
        /// テンプレート中の <c>{#}</c>, <c>{##}</c> 等のシーケンスプレースホルダの位置と長さを返す。
        /// NLog 互換のため <c>#</c> のみを受理する (<c>{0}</c> は string.Format との混同を避けるため対象外)。
        /// </summary>
        private static int FindSequencePlaceholder(string template, out int length)
        {
            length = 0;
            for (int i = 0; i < template.Length; i++)
            {
                if (template[i] != '{') continue;
                int end = template.IndexOf('}', i + 1);
                if (end < 0) return -1;
                int inner = end - i - 1;
                if (inner == 0) continue;
                bool isPlaceholder = true;
                for (int k = i + 1; k < end; k++)
                {
                    if (template[k] != '#') { isPlaceholder = false; break; }
                }
                if (isPlaceholder)
                {
                    length = end - i + 1;
                    return i;
                }
            }
            return -1;
        }

        // ───────── 古いアーカイブの掃除 ─────────

        private void CleanupOldArchives(string activePath, DateTime now)
        {
            if (_options.MaxArchiveFiles <= 0) return;
            if (_options.ArchiveNumbering == ArchiveNumbering.Rolling) return; // Rolling は自前管理

            try
            {
                List<string> archives = ListArchives(activePath, now);
                if (archives.Count <= _options.MaxArchiveFiles) return;

                // 古い順 (更新時刻昇順) でソート
                archives.Sort((a, b) =>
                {
                    var ta = SafeWriteTime(a);
                    var tb = SafeWriteTime(b);
                    return ta.CompareTo(tb);
                });

                int toDelete = archives.Count - _options.MaxArchiveFiles;
                for (int i = 0; i < toDelete; i++)
                {
                    try { System.IO.File.Delete(archives[i]); } catch { /* ignored */ }
                }
            }
            catch { /* ignored */ }
        }

        private static DateTime SafeWriteTime(string path)
        {
            try { return System.IO.File.GetLastWriteTimeUtc(path); }
            catch { return DateTime.MinValue; }
        }

        private List<string> ListArchives(string activePath, DateTime now)
        {
            var result = new List<string>();
            string archiveDir;
            string globPattern;
            bool useStrict;          // 静的 FileName 用: IsArchiveCandidate で numbering スキーム厳格チェック
            bool useDynamicFileName; // 動的 FileName 用: head/tail + middle に少なくとも1桁

            // FileName テンプレートに ${...} または {#} が入っていれば「動的ファイル名」扱い。
            // 例: FileName="logs/app_${shortdate}.log" — 日次でファイル自体が変わるため
            // activePath の baseName から組んだ glob では過去日付のファイルが引けず、
            // MaxArchiveFiles が機能しない。テンプレートから glob を組み直す。
            bool fileNameHasDynamicTokens = TemplateHasDynamicTokens(_filePath.Template);

            if (_archivePathTemplate != null)
            {
                // テンプレ中の ${...} は日付などのレンダラを含むため、
                // 現在の now で具象化してしまうと過去 period のアーカイブが glob に拾えなくなる。
                // → ディレクトリ部だけ now でレンダリングし、ファイル名部はテンプレを wildcard 化する。
                // (ディレクトリにも ${shortdate} が入っている場合は now 時点のディレクトリのみが対象。
                //  実用上「日付はファイル名に入れる」のが一般的なのでこの単純化を許容する。)
                string rendered = _archivePathTemplate.Render(LogEvent.ForPath(now));
                archiveDir = Path.GetDirectoryName(rendered) ?? string.Empty;
                if (string.IsNullOrEmpty(archiveDir)) archiveDir = ".";
                globPattern = TemplateFileNameToGlob(_archivePathTemplate.Template);
                useStrict = false;
                useDynamicFileName = false;
            }
            else if (fileNameHasDynamicTokens)
            {
                // ArchiveFileName 未設定 + FileName に動的トークン: FileName テンプレそのものを
                // wildcard 化して glob とする。日次ロール等で生まれた過去ファイルを拾えるようにする。
                archiveDir = Path.GetDirectoryName(activePath) ?? string.Empty;
                if (string.IsNullOrEmpty(archiveDir)) archiveDir = ".";
                globPattern = TemplateFileNameToGlob(_filePath.Template);
                useStrict = false;
                useDynamicFileName = true;
            }
            else
            {
                archiveDir = Path.GetDirectoryName(activePath) ?? string.Empty;
                if (string.IsNullOrEmpty(archiveDir)) archiveDir = ".";
                string baseName = Path.GetFileNameWithoutExtension(activePath);
                string ext = Path.GetExtension(activePath);
                globPattern = $"{baseName}.*{ext}";
                useStrict = true;
                useDynamicFileName = false;
            }

            if (!Directory.Exists(archiveDir)) return result;

            // 非テンプレ経路では glob `${baseName}.*${ext}` が `app.audit.log` のような
            // アーカイブとは無関係な兄弟ファイルも巻き込んでしまうため、numbering スキームに
            // 合致するもののみを残す。ArchiveFileName 経路は利用者が明示的にパターンを書いている前提、
            // 動的 FileName 経路は head/tail + 数字中間で絞り込む。
            string baseNameFilter = Path.GetFileNameWithoutExtension(activePath);
            string extFilter = Path.GetExtension(activePath);

            // 動的 FileName 経路用: glob からリテラル head / tail を抽出する。
            string dynHead = string.Empty;
            string dynTail = string.Empty;
            if (useDynamicFileName)
            {
                int firstStar = globPattern.IndexOf('*');
                int lastStar = globPattern.LastIndexOf('*');
                if (firstStar >= 0)
                {
                    dynHead = globPattern.Substring(0, firstStar);
                    dynTail = globPattern.Substring(lastStar + 1);
                }
            }

            foreach (var f in Directory.GetFiles(archiveDir, globPattern))
            {
                if (string.Equals(Path.GetFullPath(f), Path.GetFullPath(activePath), StringComparison.OrdinalIgnoreCase))
                    continue; // アクティブファイルは除外
                if (useStrict && !IsArchiveCandidate(Path.GetFileName(f), baseNameFilter, extFilter))
                    continue; // numbering スキームに合致しない兄弟ファイルは触らない
                if (useDynamicFileName && !IsDynamicFileNameCandidate(Path.GetFileName(f), dynHead, dynTail))
                    continue; // head/tail に挟まれた中間が「日付や連番らしい」ものだけ残す
                result.Add(f);
            }
            return result;
        }

        /// <summary>
        /// <see cref="_filePath"/> のテンプレートに <c>${...}</c> または <c>{#}</c> が含まれているかを返す。
        /// 動的ファイル名判定に用いる。
        /// </summary>
        private static bool TemplateHasDynamicTokens(string template)
        {
            if (string.IsNullOrEmpty(template)) return false;
            // ${...} の検出 (エスケープ \$ は除外)
            for (int i = 0; i < template.Length - 1; i++)
            {
                if (template[i] == '\\') { i++; continue; }
                if (template[i] == '$' && template[i + 1] == '{') return true;
            }
            // {#} 系のシーケンスプレースホルダ
            return FindSequencePlaceholder(template, out _) >= 0;
        }

        /// <summary>
        /// 動的 FileName 経路で拾った候補ファイル名が「アーカイブとして扱ってよい」かを判定する。
        /// head / tail に完全一致し、間の middle が非空かつ少なくとも1桁の数字を含むことを要件とする。
        /// (<c>app_${shortdate}.log</c> → <c>app_20260413.log</c> は OK、<c>app_audit.log</c> は middle に数字無しで NG)
        /// </summary>
        private static bool IsDynamicFileNameCandidate(string fileName, string head, string tail)
        {
            if (fileName.Length <= head.Length + tail.Length) return false;
            if (!fileName.StartsWith(head, StringComparison.Ordinal)) return false;
            if (!fileName.EndsWith(tail, StringComparison.Ordinal)) return false;
            int midLen = fileName.Length - head.Length - tail.Length;
            if (midLen <= 0) return false;
            for (int i = 0; i < midLen; i++)
            {
                char c = fileName[head.Length + i];
                if (c >= '0' && c <= '9') return true;
            }
            return false;
        }

        /// <summary>
        /// ファイル名が現在の <see cref="ArchiveNumbering"/> 設定で生成されうる
        /// アーカイブの命名規則に合致するかを判定する。
        /// 兄弟ファイル (例: <c>app.audit.log</c>) を retention の巻き添えにしないためのフィルタ。
        /// </summary>
        private bool IsArchiveCandidate(string fileName, string baseName, string ext)
        {
            string prefix = baseName + ".";
            if (!fileName.StartsWith(prefix, StringComparison.Ordinal)) return false;
            if (ext.Length > 0 && !fileName.EndsWith(ext, StringComparison.Ordinal)) return false;
            int midLen = fileName.Length - prefix.Length - ext.Length;
            if (midLen <= 0) return false;
            string middle = fileName.Substring(prefix.Length, midLen);

            switch (_options.ArchiveNumbering)
            {
                case ArchiveNumbering.Sequence:
                case ArchiveNumbering.Rolling:
                    // middle 全体が整数連番である必要がある
                    return int.TryParse(middle, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                        && n >= 0 && n <= MaxReasonableSequence;

                case ArchiveNumbering.Date:
                    {
                        // middle = <date>  または  <date>.<seq> (既存日付と被ったときの DateAndSequence 倒し)
                        int dotIdx = middle.IndexOf('.');
                        string datePart = dotIdx >= 0 ? middle.Substring(0, dotIdx) : middle;
                        if (!DateTime.TryParseExact(datePart, _options.ArchiveDateFormat,
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                            return false;
                        if (dotIdx >= 0)
                        {
                            string seqPart = middle.Substring(dotIdx + 1);
                            if (!int.TryParse(seqPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dn)
                                || dn < 0 || dn > MaxReasonableSequence)
                                return false;
                        }
                        return true;
                    }

                case ArchiveNumbering.DateAndSequence:
                    {
                        // middle = <date>.<seq>
                        int dot = middle.LastIndexOf('.');
                        if (dot <= 0 || dot >= middle.Length - 1) return false;
                        string dp = middle.Substring(0, dot);
                        string sp = middle.Substring(dot + 1);
                        if (!DateTime.TryParseExact(dp, _options.ArchiveDateFormat,
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                            return false;
                        return int.TryParse(sp, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dsn)
                            && dsn >= 0 && dsn <= MaxReasonableSequence;
                    }

                default:
                    return false;
            }
        }

        /// <summary>
        /// パステンプレートのファイル名部分を Win32 glob (<c>* と ?</c>) に変換する。
        /// <c>${...}</c> および <c>{#}</c> 系プレースホルダはすべて <c>*</c> に置換し、
        /// 連続する <c>*</c> は 1 つに畳む。エスケープ <c>\$</c> / <c>\\</c> はリテラル化する。
        /// </summary>
        private static string TemplateFileNameToGlob(string template)
        {
            int sep = -1;
            for (int k = template.Length - 1; k >= 0; k--)
            {
                if (template[k] == '/' || template[k] == '\\')
                {
                    // \$ / \\ のエスケープと混ざらないよう、後続が $ や \ なら path セパレータとして扱わない
                    if (template[k] == '\\' && k + 1 < template.Length
                        && (template[k + 1] == '$' || template[k + 1] == '\\'))
                        continue;
                    sep = k;
                    break;
                }
            }
            string fileName = sep >= 0 ? template.Substring(sep + 1) : template;

            var sb = new StringBuilder(fileName.Length);
            int i = 0;
            while (i < fileName.Length)
            {
                char c = fileName[i];
                if (c == '\\' && i + 1 < fileName.Length
                    && (fileName[i + 1] == '$' || fileName[i + 1] == '\\'))
                {
                    sb.Append(fileName[i + 1]);
                    i += 2;
                    continue;
                }
                if (c == '$' && i + 1 < fileName.Length && fileName[i + 1] == '{')
                {
                    // ${...} を再帰深さ込みでスキップして * に置換
                    i += 2;
                    int depth = 1;
                    while (i < fileName.Length && depth > 0)
                    {
                        char d = fileName[i];
                        if (d == '\\' && i + 1 < fileName.Length) { i += 2; continue; }
                        if (d == '$' && i + 1 < fileName.Length && fileName[i + 1] == '{') { depth++; i += 2; continue; }
                        if (d == '}') { depth--; i++; continue; }
                        i++;
                    }
                    if (sb.Length == 0 || sb[sb.Length - 1] != '*') sb.Append('*');
                    continue;
                }
                sb.Append(c);
                i++;
            }

            // {#} (シーケンスプレースホルダ) も * に
            string result = sb.ToString();
            int phPos = FindSequencePlaceholder(result, out int phLen);
            while (phPos >= 0)
            {
                result = result.Substring(0, phPos) + "*" + result.Substring(phPos + phLen);
                phPos = FindSequencePlaceholder(result, out phLen);
            }

            // 連続する * を 1 つに畳む
            while (result.IndexOf("**", StringComparison.Ordinal) >= 0)
                result = result.Replace("**", "*");

            return result;
        }

        // ───────── 時間境界 ─────────

        private static DateTime ComputeBoundary(DateTime t, ArchiveEvery every)
        {
            switch (every)
            {
                case ArchiveEvery.Year:   return new DateTime(t.Year, 1, 1);
                case ArchiveEvery.Month:  return new DateTime(t.Year, t.Month, 1);
                case ArchiveEvery.Day:    return t.Date;
                case ArchiveEvery.Hour:   return new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0);
                case ArchiveEvery.Minute: return new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0);
                case ArchiveEvery.Sunday:
                case ArchiveEvery.Monday:
                case ArchiveEvery.Tuesday:
                case ArchiveEvery.Wednesday:
                case ArchiveEvery.Thursday:
                case ArchiveEvery.Friday:
                case ArchiveEvery.Saturday:
                    {
                        var target = (DayOfWeek)(every - ArchiveEvery.Sunday);
                        int diff = ((int)t.DayOfWeek - (int)target + 7) % 7;
                        return t.Date.AddDays(-diff);
                    }
                case ArchiveEvery.None:
                default:
                    return DateTime.MinValue;
            }
        }

        // ───────── バッファ管理ヘルパー ─────────

        /// <summary>
        /// <see cref="_charBuffer"/> 先頭 <paramref name="len"/> 文字と既存文字列を char 単位で比較する。
        /// <see cref="_sb"/> から文字列化する前にパスが変わったかを判定し、
        /// 一致した場合はパス string の新規アロケーションを完全に省ける。
        /// (chunk-walk を避けるため <see cref="_sb"/> の indexer は使わない)
        /// </summary>
        private bool CharBufferEquals(int len, string? s)
        {
            if (s == null) return false;
            if (s.Length != len) return false;
            for (int i = 0; i < len; i++)
            {
                if (_charBuffer[i] != s[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// <see cref="_sb"/> の現在の内容を <see cref="_byteBuffer"/> 経由でエンコードし、
        /// <see cref="_stream"/> に書き出す。中間 <c>string</c> / <c>byte[]</c> を毎回確保しないための
        /// ホットパス用ヘルパー。バッファは必要に応じて拡張のみされる (縮小はしない)。
        /// </summary>
        /// <returns>書き込んだバイト数 (currentSize 加算用)。</returns>
        private int FlushSbToStream()
        {
            int charLen = _sb.Length;
            if (charLen == 0) return 0;

            if (_charBuffer.Length < charLen)
                _charBuffer = new char[Math.Max(charLen, _charBuffer.Length * 2)];
            _sb.CopyTo(0, _charBuffer, 0, charLen);

            int maxBytes = _options.Encoding.GetMaxByteCount(charLen);
            if (_byteBuffer.Length < maxBytes)
                _byteBuffer = new byte[Math.Max(maxBytes, _byteBuffer.Length * 2)];
            int byteLen = _options.Encoding.GetBytes(_charBuffer, 0, charLen, _byteBuffer, 0);

            _stream!.Write(_byteBuffer, 0, byteLen);
            return byteLen;
        }

        public void Dispose()
        {
            // _disposed のチェックは必ず lock の内側で行うこと (CLAUDE.md 既定ルール)。
            // 外側で見ると Write/Flush が _stream を触っている最中に CloseStream が割り込む TOCTOU の窓ができる。
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                // KeepFileOpen=false 経路では _stream は閉じているので footer 用に再オープン
                ReopenForFooter();
                CloseStream(writeFooter: true);
            }
        }

        /// <summary>
        /// <see cref="_stream"/> が閉じている状態から footer を書くためだけにアクティブファイルを
        /// append モードで再オープンする。footer 設定が無い・対象パス不明・実ファイルが既に
        /// 存在しない場合は何もしない。<see cref="CloseStream"/> の writeFooter:true 経路に流すための前処理。
        /// </summary>
        private void ReopenForFooter()
        {
            if (_stream != null) return;
            if (_footer == null) return;
            if (_currentFilePath == null) return;
            if (!System.IO.File.Exists(_currentFilePath)) return;

            try
            {
                var share = _options.ConcurrentWrites ? FileShare.ReadWrite : FileShare.Read;
                _stream = new FileStream(_currentFilePath, FileMode.Append, FileAccess.Write,
                    share, bufferSize: 4096, FileOptions.SequentialScan);
                _currentSize = _stream.Length;
            }
            catch
            {
                // 既に削除/移動された等は無視 — footer 出力をスキップするだけ
                _stream = null;
            }
        }
    }
}
