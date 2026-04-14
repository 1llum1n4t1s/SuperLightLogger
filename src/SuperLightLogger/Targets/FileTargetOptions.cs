using System;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SuperLightLogger
{
    /// <summary>
    /// NLog 互換の <c>File Target</c> を設定するオプション。
    /// <see cref="FileLoggerExtensions.AddSuperLightFile(ILoggingBuilder, Action{FileTargetOptions})"/> から構成する。
    /// </summary>
    public sealed class FileTargetOptions
    {
        /// <summary>
        /// ログファイルのパステンプレート。NLog の <c>${shortdate}</c> 等の Layout レンダラを使用可能。
        /// 例: <c>"logs/Komorebi_${date:format=yyyyMMdd}.log"</c>
        /// </summary>
        public string FileName { get; set; } = "logs/log_${date:format=yyyyMMdd}.log";

        /// <summary>
        /// 1行のレイアウトテンプレート。
        /// デフォルトは <c>yyyy-MM-dd HH:mm:ss.ffff [LEVEL] [ThreadId] message</c> ＋例外スタック。
        /// </summary>
        public string Layout { get; set; } =
            @"${date:format=yyyy-MM-dd HH\:mm\:ss.ffff} [${level:uppercase=true}] [${threadid}] ${message}${onexception:${newline}${exception:format=tostring}}";

        /// <summary>
        /// テキストエンコーディング。デフォルトは UTF-8 (BOM なし)。
        /// </summary>
        public Encoding Encoding { get; set; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// 改行コード。デフォルトは <see cref="Environment.NewLine"/>。
        /// </summary>
        public string LineEnding { get; set; } = Environment.NewLine;

        /// <summary>
        /// ファイルハンドルを書込み毎に開閉せず保持するかどうか。
        /// <c>true</c> (デフォルト) は高速だが、他プロセスからの書込みは <see cref="ConcurrentWrites"/> = true が必要。
        /// </summary>
        public bool KeepFileOpen { get; set; } = true;

        /// <summary>
        /// 書込み毎に <see cref="System.IO.FileStream.Flush()"/> を呼び出すか。
        /// </summary>
        public bool AutoFlush { get; set; } = true;

        /// <summary>
        /// ファイルパスのディレクトリが無い場合に自動作成するか。
        /// </summary>
        public bool CreateDirectories { get; set; } = true;

        /// <summary>
        /// 複数プロセスからの同時書込みを許可するか。<c>true</c> なら <see cref="System.IO.FileShare.ReadWrite"/> で開く。
        /// </summary>
        public bool ConcurrentWrites { get; set; } = false;

        /// <summary>
        /// 書込み毎にファイル内容を完全に置き換えるか。
        /// </summary>
        public bool ReplaceFileContentsOnEachWrite { get; set; } = false;

        /// <summary>
        /// ファイルが新規作成された際に最初に書き出すヘッダーレイアウト。
        /// </summary>
        public string? Header { get; set; }

        /// <summary>
        /// ファイルがクローズされる際に最後に書き出すフッターレイアウト。
        /// </summary>
        public string? Footer { get; set; }

        // ─── アーカイブ ───

        /// <summary>
        /// 時間ベースで自動アーカイブを行う粒度。<see cref="ArchiveEvery.None"/> (デフォルト) で無効。
        /// </summary>
        public ArchiveEvery ArchiveEvery { get; set; } = ArchiveEvery.None;

        /// <summary>
        /// アーカイブを起動するファイルサイズ閾値 (バイト)。0 で無効。デフォルトは 1 MB。
        /// </summary>
        public long ArchiveAboveSize { get; set; } = 1L * 1024 * 1024;

        /// <summary>
        /// アーカイブファイル名のテンプレート。<c>{#}</c> がシーケンス番号に置換される。
        /// <c>null</c> の場合は <see cref="FileName"/> から <c>{base}.{N}{ext}</c> 形式で自動生成する。
        /// </summary>
        public string? ArchiveFileName { get; set; }

        /// <summary>
        /// アーカイブ番号付け方式。
        /// </summary>
        public ArchiveNumbering ArchiveNumbering { get; set; } = ArchiveNumbering.Sequence;

        /// <summary>
        /// 保持するアーカイブの最大数。古いものから削除される。0 で無制限。デフォルトは 10。
        /// </summary>
        public int MaxArchiveFiles { get; set; } = 10;

        /// <summary>
        /// アーカイブ番号付けで <see cref="ArchiveNumbering.Date"/> / <see cref="ArchiveNumbering.DateAndSequence"/>
        /// を使う場合の日付フォーマット。
        /// </summary>
        public string ArchiveDateFormat { get; set; } = "yyyyMMdd";

        // ─── 非同期 ───

        /// <summary>
        /// 非同期書込みを有効化するか。<c>true</c> ならバックグラウンドスレッドで書込みを行う。
        /// </summary>
        public bool Async { get; set; } = false;

        /// <summary>
        /// 非同期キューのバッファサイズ。これを超えるとブロッキング (または破棄)。
        /// </summary>
        public int AsyncBufferSize { get; set; } = 10000;

        /// <summary>
        /// 非同期キューが満杯のとき、新しいログを破棄するか (true) ブロックするか (false)。
        /// </summary>
        public bool AsyncDiscardOnFull { get; set; } = false;

        /// <summary>
        /// 非同期書込みでフラッシュを行う最大間隔。
        /// </summary>
        public TimeSpan AsyncFlushInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// 出力対象とする最低ログレベル。
        /// </summary>
        public LogLevel MinLevel { get; set; } = LogLevel.Trace;
    }

    /// <summary>
    /// 時間ベースのアーカイブ粒度 (NLog の <c>ArchiveEvery</c> 互換)。
    /// </summary>
    public enum ArchiveEvery
    {
        /// <summary>時間ベースのアーカイブを行わない。</summary>
        None = 0,
        /// <summary>年単位 (1月1日 00:00 で切り替え)。</summary>
        Year,
        /// <summary>月単位 (1日 00:00 で切り替え)。</summary>
        Month,
        /// <summary>日単位 (毎日 00:00 で切り替え)。</summary>
        Day,
        /// <summary>時間単位 (毎時 0分 で切り替え)。</summary>
        Hour,
        /// <summary>分単位。</summary>
        Minute,
        /// <summary>毎週日曜日にアーカイブ。</summary>
        Sunday,
        /// <summary>毎週月曜日にアーカイブ。</summary>
        Monday,
        /// <summary>毎週火曜日にアーカイブ。</summary>
        Tuesday,
        /// <summary>毎週水曜日にアーカイブ。</summary>
        Wednesday,
        /// <summary>毎週木曜日にアーカイブ。</summary>
        Thursday,
        /// <summary>毎週金曜日にアーカイブ。</summary>
        Friday,
        /// <summary>毎週土曜日にアーカイブ。</summary>
        Saturday,
    }

    /// <summary>
    /// アーカイブファイルの番号付け方式 (NLog の <c>ArchiveNumberingMode</c> 互換)。
    /// </summary>
    public enum ArchiveNumbering
    {
        /// <summary>連番方式: <c>file.1.log</c>, <c>file.2.log</c>, ... 既存最大番号 +1 でアーカイブ。</summary>
        Sequence = 0,
        /// <summary>ローリング方式: <c>file.0.log</c> が最新、アーカイブごとに番号がシフトする。</summary>
        Rolling,
        /// <summary>日付方式: <c>file.20260414.log</c></summary>
        Date,
        /// <summary>日付＋連番方式: <c>file.20260414.1.log</c></summary>
        DateAndSequence,
    }
}
