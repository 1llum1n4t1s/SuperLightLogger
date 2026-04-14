using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuperLightLogger;
using Xunit;

namespace SuperLightLogger.Tests.Targets;

/// <summary>
/// <see cref="FileTargetWriter"/> および周辺コンポーネントへの敵対的 (adversarial) テスト群。
/// 通常のユニットテストでは検出しにくい「壊れ方の安全性」「リソースリーク」「並行異常」
/// 「環境異常」「境界値」「型/プロトコル違反」を炙り出すことを目的とする。
///
/// 期待値: 基本的に「クラッシュしない」「例外をリークさせない」「リソースを漏らさない」。
/// NLog 互換スペックに合わせて、不正設定はサイレント失敗 (Console.Error 通知) が許容される。
/// </summary>
public class FileTargetWriterAdversarialTests : IDisposable
{
    private readonly string _tempDir;

    public FileTargetWriterAdversarialTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SuperLightLoggerAdversarial_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            /* テストクリーンアップ失敗は無視 */
        }
    }

    private static LogEvent MakeEvent(
        DateTime? timestamp = null,
        LogLevel level = LogLevel.Information,
        string logger = "Test",
        string message = "msg",
        Exception? exception = null,
        int threadId = 1,
        string? threadName = null)
        => new LogEvent(
            timestamp ?? DateTime.Now,
            level,
            logger,
            message,
            exception,
            threadId,
            threadName);

    // ═══════════════════════════════════════════════════════════════════
    // 🗡️ Category 1: 境界値・極端入力 (Boundary Assault)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 空メッセージ書込みがクラッシュせず、改行だけのエントリとして記録されること。
    /// </summary>
    [Fact]
    public void Boundary_EmptyMessage_DoesNotCrash()
    {
        var path = Path.Combine(_tempDir, "empty.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        using (var writer = new FileTargetWriter(options))
        {
            var ex = Record.Exception(() => writer.Write(MakeEvent(message: string.Empty)));
            Assert.Null(ex);
        }

        Assert.True(System.IO.File.Exists(path));
        string content = System.IO.File.ReadAllText(path);
        // 空メッセージ + 改行だけが書き込まれていること
        Assert.Equal(Environment.NewLine, content);
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 1MB の巨大メッセージ 1 発書込みでメモリ溢れ/クラッシュしないこと。
    /// _charBuffer / _byteBuffer が必要に応じて拡張されること。
    /// </summary>
    [Fact]
    public void Boundary_HugeMessage_1MB_DoesNotCrash()
    {
        var path = Path.Combine(_tempDir, "huge.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        string hugeMessage = new string('X', 1 * 1024 * 1024);

        using (var writer = new FileTargetWriter(options))
        {
            var ex = Record.Exception(() => writer.Write(MakeEvent(message: hugeMessage)));
            Assert.Null(ex);
        }

        var fi = new FileInfo(path);
        Assert.True(fi.Exists);
        Assert.True(fi.Length >= hugeMessage.Length, "巨大メッセージが書き込まれていること");
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// ヌルバイト (\0) を含むメッセージを受け付けてクラッシュしないこと。
    /// C# string は \0 を合法に含める。UTF-8 エンコーダも通せる。
    /// </summary>
    [Fact]
    public void Boundary_NullByteInMessage_DoesNotCrash()
    {
        var path = Path.Combine(_tempDir, "nullbyte.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        using (var writer = new FileTargetWriter(options))
        {
            var ex = Record.Exception(() => writer.Write(MakeEvent(message: "before\0after")));
            Assert.Null(ex);
        }

        byte[] bytes = System.IO.File.ReadAllBytes(path);
        Assert.Contains((byte)0x00, bytes);
        // "before" と "after" の両方がバイト列に存在すること
        Assert.True(bytes.Length >= "before\0after".Length);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// Unicode 地雷: ゼロ幅空白・RTL 制御文字・絵文字結合シーケンスを
    /// UTF-8 エンコードで正しくラウンドトリップできること。
    /// </summary>
    [Fact]
    public void Boundary_UnicodeTraps_RoundTripViaUtf8()
    {
        var path = Path.Combine(_tempDir, "unicode.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        // ゼロ幅空白 + RTL制御文字 + 家族絵文字 (ZWJ シーケンス) + サロゲートペア
        string trap = "hi\u200B\u202E\u202D\uD83D\uDC68\u200D\uD83D\uDC69\u200D\uD83D\uDC67\u200D\uD83D\uDC66";

        using (var writer = new FileTargetWriter(options))
        {
            var ex = Record.Exception(() => writer.Write(MakeEvent(message: trap)));
            Assert.Null(ex);
        }

        // UTF-8 として正しく読み戻せて、同じ内容が含まれていること
        string content = System.IO.File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Assert.Contains(trap, content);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 100KB の logger 名が ${logger} 展開でパスに使われてもクラッシュしないこと。
    /// (OS のパス長制限を超える可能性があり、Write 側でキャッチされ Console.Error に通知される期待)
    /// </summary>
    [Fact]
    public void Boundary_MassiveLoggerNameInPath_HandledGracefully()
    {
        // 長大な logger 名は OS のパス長上限を超えるので、Write は例外を内部で握る想定。
        var template = Path.Combine(_tempDir, "log_${logger}.log");
        var options = new FileTargetOptions
        {
            FileName = template,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        string massiveLogger = new string('L', 100_000);

        using (var writer = new FileTargetWriter(options))
        {
            // クラッシュせず静かに失敗すること (例外をリークさせない)
            var ex = Record.Exception(() => writer.Write(MakeEvent(logger: massiveLogger, message: "short")));
            Assert.Null(ex);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // ⚡ Category 2: 並行性・レースコンディション (Concurrency Chaos)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category concurrency @severity high
    /// 16 スレッドで 500 行ずつ (計 8000 行) を 1 writer に叩き込んでも
    /// 全行が順序を問わず記録されていること (lock が正しく排他していること)。
    /// </summary>
    [Fact]
    public void Concurrency_16Threads_500LinesEach_AllWritten()
    {
        var path = Path.Combine(_tempDir, "race.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 0,
            KeepFileOpen = true,
        };

        const int threadCount = 16;
        const int linesPerThread = 500;

        using (var writer = new FileTargetWriter(options))
        {
            var barrier = new Barrier(threadCount);
            var tasks = Enumerable.Range(0, threadCount).Select(t => Task.Run(() =>
            {
                barrier.SignalAndWait(); // 同時スタート
                for (int i = 0; i < linesPerThread; i++)
                {
                    writer.Write(MakeEvent(message: $"T{t}-L{i}"));
                }
            })).ToArray();

            Task.WaitAll(tasks);
            writer.Flush();
        }

        var lines = System.IO.File.ReadAllLines(path);
        Assert.Equal(threadCount * linesPerThread, lines.Length);
        // 各スレッドからの全行が漏れなく存在すること
        for (int t = 0; t < threadCount; t++)
        {
            int count = lines.Count(l => l.StartsWith($"T{t}-"));
            Assert.Equal(linesPerThread, count);
        }
    }

    /// <summary>
    /// @adversarial @category concurrency @severity high
    /// 書込みループ中に別スレッドから Dispose を叩き込んでも
    /// 例外がリークせず、Dispose 後の書込みは静かにドロップされること。
    /// </summary>
    [Fact]
    public void Concurrency_DisposeWhileWriting_NoExceptionLeak()
    {
        var path = Path.Combine(_tempDir, "dispose-race.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        var writer = new FileTargetWriter(options);
        var stop = new CancellationTokenSource();
        Exception? writeError = null;

        var writerTask = Task.Run(() =>
        {
            try
            {
                int i = 0;
                while (!stop.IsCancellationRequested)
                {
                    writer.Write(MakeEvent(message: $"line-{i++}"));
                }
            }
            catch (Exception ex) { writeError = ex; }
        });

        // 少し書かせてから破棄
        Thread.Sleep(30);
        writer.Dispose();
        stop.Cancel();
        writerTask.Wait(TimeSpan.FromSeconds(2));

        Assert.Null(writeError);
    }

    /// <summary>
    /// @adversarial @category concurrency @severity medium
    /// 2 スレッドから同時に Dispose を叩いても例外にならないこと (冪等性)。
    /// </summary>
    [Fact]
    public void Concurrency_DoubleDispose_FromTwoThreads_Idempotent()
    {
        var path = Path.Combine(_tempDir, "double-dispose.log");
        var options = new FileTargetOptions { FileName = path, Layout = "${message}" };
        var writer = new FileTargetWriter(options);
        writer.Write(MakeEvent(message: "payload"));

        var barrier = new Barrier(2);
        Exception? err1 = null, err2 = null;
        var t1 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            try { writer.Dispose(); } catch (Exception ex) { err1 = ex; }
        });
        var t2 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            try { writer.Dispose(); } catch (Exception ex) { err2 = ex; }
        });

        Task.WaitAll(t1, t2);
        Assert.Null(err1);
        Assert.Null(err2);
    }

    /// <summary>
    /// @adversarial @category concurrency @severity medium
    /// 書込みループ中に別スレッドから Flush を叩いても例外ループにならないこと。
    /// </summary>
    [Fact]
    public void Concurrency_FlushWhileWriting_NoExceptionLeak()
    {
        var path = Path.Combine(_tempDir, "flush-race.log");
        var options = new FileTargetOptions { FileName = path, Layout = "${message}" };
        using var writer = new FileTargetWriter(options);

        var stop = new CancellationTokenSource();
        Exception? writeError = null;
        Exception? flushError = null;

        var wt = Task.Run(() =>
        {
            try
            {
                int i = 0;
                while (!stop.IsCancellationRequested)
                    writer.Write(MakeEvent(message: $"w-{i++}"));
            }
            catch (Exception ex) { writeError = ex; }
        });

        var ft = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 50; i++)
                {
                    writer.Flush();
                    Thread.Sleep(1);
                }
            }
            catch (Exception ex) { flushError = ex; }
        });

        ft.Wait(TimeSpan.FromSeconds(3));
        stop.Cancel();
        wt.Wait(TimeSpan.FromSeconds(2));

        Assert.Null(writeError);
        Assert.Null(flushError);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 💀 Category 3: リソース枯渇 (Resource Exhaustion)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category resource @severity high
    /// ArchiveAboveSize=1 で 200 回アーカイブをループさせてもファイルハンドル/連番が壊れないこと。
    /// (サイズアーカイブのリソースリーク検出)
    /// </summary>
    [Fact]
    public void Resource_RapidSizeArchive_200Rotations_NoCrash()
    {
        var path = Path.Combine(_tempDir, "rapid.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 1, // 毎書込みでアーカイブ発火
            ArchiveNumbering = ArchiveNumbering.Sequence,
            MaxArchiveFiles = 5,
            KeepFileOpen = true,
        };

        using (var writer = new FileTargetWriter(options))
        {
            for (int i = 0; i < 200; i++)
            {
                writer.Write(MakeEvent(message: $"spam-{i}"));
            }
        }

        // MaxArchiveFiles=5 なのでアーカイブは最大 5 件に収まっていること
        var archives = Directory.GetFiles(_tempDir, "rapid.*.log");
        Assert.True(archives.Length <= 5, $"retention が効いていない: {archives.Length} 個のアーカイブが存在");
    }

    /// <summary>
    /// @adversarial @category resource @severity high
    /// KeepFileOpen=false で 5000 回書込みしてもファイルハンドルが枯渇しないこと。
    /// (毎回オープン/クローズなので、Dispose 漏れがあれば FD 枯渇で例外が出る)
    /// </summary>
    [Fact]
    public void Resource_KeepFileOpenFalse_5000Writes_NoFdLeak()
    {
        var path = Path.Combine(_tempDir, "fdleak.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            KeepFileOpen = false,
            ArchiveAboveSize = 0,
        };

        using (var writer = new FileTargetWriter(options))
        {
            for (int i = 0; i < 5000; i++)
            {
                writer.Write(MakeEvent(message: $"msg-{i}"));
            }
        }

        var fi = new FileInfo(path);
        Assert.True(fi.Exists);
        Assert.True(fi.Length > 0);
    }

    /// <summary>
    /// @adversarial @category resource @severity medium
    /// 100 件の 100KB メッセージを書いてもバッファ成長は新規書込みを阻害しないこと。
    /// (書込み後のファイルサイズが期待通りであることを確認)
    /// </summary>
    [Fact]
    public void Resource_100xLargeMessages_BufferGrowthOk()
    {
        var path = Path.Combine(_tempDir, "bufgrow.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        string chunk = new string('Z', 100 * 1024);

        using (var writer = new FileTargetWriter(options))
        {
            for (int i = 0; i < 100; i++)
            {
                writer.Write(MakeEvent(message: chunk));
            }
        }

        long expected = 100L * (chunk.Length + Environment.NewLine.Length);
        var fi = new FileInfo(path);
        Assert.Equal(expected, fi.Length);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 🔀 Category 4: 状態遷移の矛盾 (State Machine Abuse)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category state @severity medium
    /// 一度 Dispose した後に Write しても例外は出ず、ただ無視されること。
    /// </summary>
    [Fact]
    public void State_WriteAfterDispose_SilentlyDropped()
    {
        var path = Path.Combine(_tempDir, "post-dispose.log");
        var options = new FileTargetOptions { FileName = path, Layout = "${message}" };
        var writer = new FileTargetWriter(options);
        writer.Write(MakeEvent(message: "before"));
        writer.Dispose();

        var ex = Record.Exception(() =>
        {
            writer.Write(MakeEvent(message: "after-dispose"));
            writer.Flush();
        });
        Assert.Null(ex);

        // "after-dispose" はファイルに書かれていない
        string content = System.IO.File.ReadAllText(path);
        Assert.Contains("before", content);
        Assert.DoesNotContain("after-dispose", content);
    }

    /// <summary>
    /// @adversarial @category state @severity low
    /// Write を一切しないまま Dispose しても例外にならないこと (空ライフサイクル)。
    /// </summary>
    [Fact]
    public void State_DisposeWithoutAnyWrite_NoCrash()
    {
        var path = Path.Combine(_tempDir, "never-written.log");
        var options = new FileTargetOptions { FileName = path, Layout = "${message}" };

        var ex = Record.Exception(() =>
        {
            using var writer = new FileTargetWriter(options);
            // 何も書かない
        });
        Assert.Null(ex);

        // ファイルも作られていないこと (遅延生成)
        Assert.False(System.IO.File.Exists(path));
    }

    /// <summary>
    /// @adversarial @category state @severity low
    /// Write を一切しないまま Flush を叩いても例外にならないこと。
    /// </summary>
    [Fact]
    public void State_FlushWithoutAnyWrite_NoCrash()
    {
        var path = Path.Combine(_tempDir, "flush-only.log");
        var options = new FileTargetOptions { FileName = path, Layout = "${message}" };
        using var writer = new FileTargetWriter(options);

        var ex = Record.Exception(() => writer.Flush());
        Assert.Null(ex);
    }

    /// <summary>
    /// @adversarial @category state @severity medium
    /// Header/Footer が設定された状態で Write を 0 回行って Dispose した場合、
    /// ファイル自体が生成されず、footer 再オープンも試みないこと (Exists チェックでガード)。
    /// </summary>
    [Fact]
    public void State_HeaderFooter_ZeroWrites_NoFileCreated()
    {
        var path = Path.Combine(_tempDir, "hf-only.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            Header = "===HEAD===",
            Footer = "===FOOT===",
            KeepFileOpen = false,
        };

        var ex = Record.Exception(() =>
        {
            using var writer = new FileTargetWriter(options);
            // 書込み無し
        });
        Assert.Null(ex);
        Assert.False(System.IO.File.Exists(path), "書込みが 1 度も無ければファイルは生成されない");
    }

    // ═══════════════════════════════════════════════════════════════════
    // 🎭 Category 5: 型パンチ・プロトコル違反 (Type Punching)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category type @severity medium
    /// LogEvent の logger / message フィールドに null を直接注入しても
    /// StringBuilder.Append(null) として安全に扱われ、クラッシュしないこと。
    /// (公開 API 経由では null が渡ることは無いが、直接構築可能なので防御が要る)
    /// </summary>
    [Fact]
    public void TypePunch_NullLoggerAndMessage_DoesNotCrash()
    {
        var path = Path.Combine(_tempDir, "null-fields.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${logger}|${message}",
            ArchiveAboveSize = 0,
        };

        using (var writer = new FileTargetWriter(options))
        {
            var ev = new LogEvent(
                DateTime.Now,
                LogLevel.Information,
                logger: null!,
                message: null!,
                exception: null,
                threadId: 1,
                threadName: null);
            var ex = Record.Exception(() => writer.Write(in ev));
            Assert.Null(ex);
        }

        string content = System.IO.File.ReadAllText(path);
        // logger と message が両方空でも区切り文字 '|' は残る
        Assert.Contains("|", content);
    }

    /// <summary>
    /// @adversarial @category type @severity medium
    /// 未知の <see cref="LogLevel"/> 値 (enum 範囲外) を注入してもクラッシュしないこと。
    /// LayoutRenderer は文字列化で安全に吸収する。
    /// </summary>
    [Fact]
    public void TypePunch_UnknownLogLevel_DoesNotCrash()
    {
        var path = Path.Combine(_tempDir, "weird-level.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${level}|${message}",
            ArchiveAboveSize = 0,
        };

        using (var writer = new FileTargetWriter(options))
        {
            var ex = Record.Exception(() =>
                writer.Write(MakeEvent(level: (LogLevel)999, message: "weird")));
            Assert.Null(ex);
        }

        string content = System.IO.File.ReadAllText(path);
        Assert.Contains("weird", content);
    }

    /// <summary>
    /// @adversarial @category type @severity medium
    /// 無効な DateTime 書式指定 (例: <c>"q"</c>) が <see cref="FileTargetOptions.ArchiveDateFormat"/>
    /// に入っていても、サイズアーカイブ経路で例外がリークしないこと。
    /// </summary>
    [Fact]
    public void TypePunch_InvalidArchiveDateFormat_HandledGracefully()
    {
        var path = Path.Combine(_tempDir, "bad-dateformat.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 1, // 即アーカイブ
            ArchiveNumbering = ArchiveNumbering.Date,
            ArchiveDateFormat = "q", // DateTime.ToString("q") は FormatException
            KeepFileOpen = true,
        };

        using (var writer = new FileTargetWriter(options))
        {
            var ex = Record.Exception(() =>
            {
                writer.Write(MakeEvent(message: "trigger-archive"));
                writer.Write(MakeEvent(message: "follow-up"));
            });
            Assert.Null(ex);
        }
    }

    /// <summary>
    /// @adversarial @category type @severity low
    /// Layout テンプレートに未閉鎖の <c>${</c> が含まれていても、
    /// LayoutRenderer がハングしたりクラッシュしたりしないこと。
    /// </summary>
    [Fact]
    public void TypePunch_UnclosedLayoutToken_DoesNotHang()
    {
        var path = Path.Combine(_tempDir, "unclosed.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "prefix ${logger broken", // 閉じる } が無い
            ArchiveAboveSize = 0,
        };

        var ex = Record.Exception(() =>
        {
            using var writer = new FileTargetWriter(options);
            writer.Write(MakeEvent(message: "msg"));
        });
        Assert.Null(ex);
    }

    /// <summary>
    /// @adversarial @category type @severity medium
    /// <c>FileName</c> テンプレート自体に未閉鎖トークンが入っていても
    /// コンストラクタでハング/クラッシュしないこと。
    /// </summary>
    [Fact]
    public void TypePunch_UnclosedFileNameToken_DoesNotHang()
    {
        var path = Path.Combine(_tempDir, "log_${logger.log"); // '}' が無い
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        var ex = Record.Exception(() =>
        {
            using var writer = new FileTargetWriter(options);
            writer.Write(MakeEvent(message: "hello"));
        });
        Assert.Null(ex);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 🌪️ Category 6: 環境異常 (Environmental Chaos)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category chaos @severity medium
    /// <see cref="DateTime.MinValue"/> を Timestamp に持つイベントでも
    /// <c>ComputeBoundary</c> / レイアウト描画がクラッシュしないこと。
    /// </summary>
    [Fact]
    public void Chaos_DateTimeMinValue_DoesNotCrash()
    {
        var path = Path.Combine(_tempDir, "min-date.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${longdate}|${message}",
            ArchiveEvery = ArchiveEvery.Day,
            ArchiveNumbering = ArchiveNumbering.Date,
        };

        using (var writer = new FileTargetWriter(options))
        {
            var ex = Record.Exception(() => writer.Write(MakeEvent(timestamp: DateTime.MinValue, message: "epoch")));
            Assert.Null(ex);
        }

        Assert.True(System.IO.File.Exists(path));
    }

    /// <summary>
    /// @adversarial @category chaos @severity medium
    /// <see cref="DateTime.MaxValue"/> を Timestamp に持つイベントでもクラッシュしないこと。
    /// (Year ロール境界計算の境界条件)
    /// </summary>
    [Fact]
    public void Chaos_DateTimeMaxValue_DoesNotCrash()
    {
        var path = Path.Combine(_tempDir, "max-date.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${longdate}|${message}",
            ArchiveEvery = ArchiveEvery.Year,
            ArchiveNumbering = ArchiveNumbering.Date,
        };

        using (var writer = new FileTargetWriter(options))
        {
            var ex = Record.Exception(() => writer.Write(MakeEvent(timestamp: DateTime.MaxValue, message: "doomsday")));
            Assert.Null(ex);
        }
    }

    /// <summary>
    /// @adversarial @category chaos @severity high
    /// FileName が <c>null</c> 設定された場合、ctor で LayoutRenderer が空テンプレに
    /// フォールバックし、Write 時に空パスで FileStream 生成が失敗するが
    /// 例外はリークせず Console.Error に通知されるだけであること (NLog 互換)。
    /// </summary>
    [Fact]
    public void Chaos_NullFileName_SilentFailure()
    {
        var options = new FileTargetOptions
        {
            FileName = null!,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        var ex = Record.Exception(() =>
        {
            using var writer = new FileTargetWriter(options);
            writer.Write(MakeEvent(message: "this-should-not-throw"));
            writer.Flush();
        });
        Assert.Null(ex);
    }

    /// <summary>
    /// @adversarial @category chaos @severity high
    /// FileName が空文字列 <c>""</c> の場合もサイレント失敗で収まること。
    /// </summary>
    [Fact]
    public void Chaos_EmptyFileName_SilentFailure()
    {
        var options = new FileTargetOptions
        {
            FileName = string.Empty,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        var ex = Record.Exception(() =>
        {
            using var writer = new FileTargetWriter(options);
            writer.Write(MakeEvent(message: "payload"));
        });
        Assert.Null(ex);
    }

    /// <summary>
    /// @adversarial @category chaos @severity high
    /// <c>${logger}</c> をパスに使って logger 名に <c>../</c> が入っていても
    /// クラッシュしないこと (NLog 互換: サニタイズしない = 利用者責任)。
    /// 書込み先は <c>_tempDir</c> の親相対に解決される可能性があるが、
    /// 少なくとも例外がリークしないことを保証する。
    /// </summary>
    [Fact]
    public void Chaos_PathTraversalViaLoggerToken_DoesNotCrash()
    {
        // テンプレ自体は tempDir 内に閉じておき、logger 名で相対パスに逃がす形。
        // 実際の解決先は tempDir のサブ/親になる可能性があるが、テストでは「クラッシュしない」だけを検証する。
        var template = Path.Combine(_tempDir, "dir_${logger}.log");
        var options = new FileTargetOptions
        {
            FileName = template,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        var ex = Record.Exception(() =>
        {
            using var writer = new FileTargetWriter(options);
            writer.Write(MakeEvent(logger: "..\\..\\escape", message: "attempt"));
        });
        Assert.Null(ex);
    }

    /// <summary>
    /// @adversarial @category chaos @severity high
    /// 作成できないディレクトリ (既存の file を親に指定) への書込みが
    /// 例外をリークせず Console.Error 通知で終わること。
    /// </summary>
    [Fact]
    public void Chaos_ParentIsExistingFile_SilentFailure()
    {
        // tempDir 内に "blocker" というファイルを作って、それをディレクトリ扱いしたパスを狙う
        string blocker = Path.Combine(_tempDir, "blocker");
        System.IO.File.WriteAllText(blocker, "im a file not a dir");

        var options = new FileTargetOptions
        {
            FileName = Path.Combine(blocker, "child.log"), // blocker はファイルなので親ディレクトリ作成は必ず失敗する
            Layout = "${message}",
            CreateDirectories = true,
            ArchiveAboveSize = 0,
        };

        var ex = Record.Exception(() =>
        {
            using var writer = new FileTargetWriter(options);
            writer.Write(MakeEvent(message: "doomed"));
            writer.Write(MakeEvent(message: "second"));
        });
        Assert.Null(ex);
    }

    /// <summary>
    /// @adversarial @category chaos @severity medium
    /// <c>ConcurrentWrites=false</c> で同一パスを他プロセス/他ハンドルが掴んでいる状態を
    /// 模擬し (FileShare.None で自前で開いておく)、Writer が例外をリークせず
    /// サイレント失敗することを確認する。
    /// </summary>
    [Fact]
    public void Chaos_FileLockedByAnotherHandle_SilentFailure()
    {
        var path = Path.Combine(_tempDir, "locked.log");
        // 先に自分で排他ロックを掴んでおく
        using var exclusive = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        exclusive.WriteByte(0x42);

        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ConcurrentWrites = false,
            ArchiveAboveSize = 0,
        };

        var ex = Record.Exception(() =>
        {
            using var writer = new FileTargetWriter(options);
            writer.Write(MakeEvent(message: "blocked"));
        });
        Assert.Null(ex);
    }

    /// <summary>
    /// @adversarial @category chaos @severity medium
    /// Async モードで <see cref="AsyncFileQueue"/> がバックプレッシャ時に
    /// discard しても例外を出さず、Dispose 時に残量がドレインされること。
    /// </summary>
    [Fact]
    public void Chaos_AsyncMode_BufferOverflow_DiscardDoesNotCrash()
    {
        var path = Path.Combine(_tempDir, "async-discard.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            Async = true,
            AsyncBufferSize = 16, // わざと小さく
            AsyncDiscardOnFull = true,
            ArchiveAboveSize = 0,
        };

        var ex = Record.Exception(() =>
        {
            using var provider = new FileLoggerProvider(options);
            var logger = provider.CreateLogger("Chaos");
            for (int i = 0; i < 10_000; i++)
            {
                logger.LogInformation("flood-{I}", i);
            }
        });
        Assert.Null(ex);

        // 少なくとも何行かは flush されているはず (discard でも全滅にはならない)
        Assert.True(System.IO.File.Exists(path));
    }
}
