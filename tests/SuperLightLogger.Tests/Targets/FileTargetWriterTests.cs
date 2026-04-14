using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using SuperLightLogger;
using Xunit;

namespace SuperLightLogger.Tests.Targets;

/// <summary>
/// <see cref="FileTargetWriter"/> の同期書込み・アーカイブ・ローリング動作テスト。
/// </summary>
public class FileTargetWriterTests : IDisposable
{
    private readonly string _tempDir;

    public FileTargetWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SuperLightLoggerTests_" + Guid.NewGuid().ToString("N"));
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
        Exception? exception = null)
        => new LogEvent(
            timestamp ?? DateTime.Now,
            level,
            logger,
            message,
            exception,
            1,
            null);

    // ───────────── 基本書込み ─────────────

    [Fact]
    public void Write_SingleEvent_CreatesFileWithLine()
    {
        var path = Path.Combine(_tempDir, "basic.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(message: "hello"));
            writer.Flush();
        }

        Assert.True(System.IO.File.Exists(path));
        string content = System.IO.File.ReadAllText(path);
        Assert.Equal("hello" + Environment.NewLine, content);
    }

    [Fact]
    public void Write_MultipleEvents_AppendsLines()
    {
        var path = Path.Combine(_tempDir, "multi.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(message: "line1"));
            writer.Write(MakeEvent(message: "line2"));
            writer.Write(MakeEvent(message: "line3"));
        }

        var lines = System.IO.File.ReadAllLines(path);
        Assert.Equal(new[] { "line1", "line2", "line3" }, lines);
    }

    [Fact]
    public void Write_Utf8Encoding_NoBom()
    {
        var path = Path.Combine(_tempDir, "utf8.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(message: "あいうえお"));
        }

        var bytes = System.IO.File.ReadAllBytes(path);
        // BOM (EF BB BF) は出ない
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Contains("あいうえお", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Write_CreatesParentDirectory_WhenMissing()
    {
        var path = Path.Combine(_tempDir, "sub", "nested", "log.txt");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(message: "x"));
        }

        Assert.True(System.IO.File.Exists(path));
    }

    // ───────────── パステンプレート (日付ベースのファイル切替) ─────────────

    [Fact]
    public void DateTemplate_ChangesPath_WhenDayChanges()
    {
        var template = Path.Combine(_tempDir, "log_${date:format=yyyyMMdd}.log");
        var options = new FileTargetOptions
        {
            FileName = template,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        var day1 = new DateTime(2026, 4, 14, 23, 59, 59);
        var day2 = new DateTime(2026, 4, 15, 0, 0, 1);

        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(timestamp: day1, message: "yesterday"));
            writer.Write(MakeEvent(timestamp: day2, message: "today"));
        }

        var p1 = Path.Combine(_tempDir, "log_20260414.log");
        var p2 = Path.Combine(_tempDir, "log_20260415.log");
        Assert.True(System.IO.File.Exists(p1));
        Assert.True(System.IO.File.Exists(p2));
        Assert.Equal("yesterday" + Environment.NewLine, System.IO.File.ReadAllText(p1));
        Assert.Equal("today" + Environment.NewLine, System.IO.File.ReadAllText(p2));
    }

    // ───────────── サイズベースのアーカイブ ─────────────

    [Fact]
    public void SizeArchive_RollsOverWhenExceeded_SequenceNumbering()
    {
        var path = Path.Combine(_tempDir, "size.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 20, // 20バイト
            ArchiveNumbering = ArchiveNumbering.Sequence,
            MaxArchiveFiles = 0,   // 削除なし
        };

        using (var writer = new FileTargetWriter(options))
        {
            // 1 行で 20 バイト超え
            writer.Write(MakeEvent(message: "0123456789012345678901234567890")); // 31文字+改行
            writer.Write(MakeEvent(message: "next"));
        }

        var archive1 = Path.Combine(_tempDir, "size.1.log");
        Assert.True(System.IO.File.Exists(archive1));
        Assert.True(System.IO.File.Exists(path));
        Assert.Contains("01234", System.IO.File.ReadAllText(archive1));
        Assert.Equal("next" + Environment.NewLine, System.IO.File.ReadAllText(path));
    }

    [Fact]
    public void SizeArchive_SequenceIncrements_OnMultipleArchives()
    {
        var path = Path.Combine(_tempDir, "seq.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            // 1 メッセージ = "X" + NewLine。Windows なら 3 バイト、Unix なら 2 バイト。
            // 1 メッセージで超える閾値にしておけば、毎 write でアーカイブが発生する。
            ArchiveAboveSize = 1,
            ArchiveNumbering = ArchiveNumbering.Sequence,
            MaxArchiveFiles = 0,
        };

        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(message: "A"));
            writer.Write(MakeEvent(message: "B"));
            writer.Write(MakeEvent(message: "C"));
        }

        // A → seq.1, B → seq.2, C → seq.log (アクティブ)
        Assert.True(System.IO.File.Exists(Path.Combine(_tempDir, "seq.1.log")));
        Assert.True(System.IO.File.Exists(Path.Combine(_tempDir, "seq.2.log")));
        Assert.Contains("A", System.IO.File.ReadAllText(Path.Combine(_tempDir, "seq.1.log")));
        Assert.Contains("B", System.IO.File.ReadAllText(Path.Combine(_tempDir, "seq.2.log")));
    }

    // ───────────── MaxArchiveFiles による掃除 ─────────────

    [Fact]
    public void MaxArchiveFiles_DeletesOldestFirst()
    {
        var path = Path.Combine(_tempDir, "cap.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 5,
            ArchiveNumbering = ArchiveNumbering.Sequence,
            MaxArchiveFiles = 2,
        };

        using (var writer = new FileTargetWriter(options))
        {
            // 5件アーカイブを発生させる
            for (int i = 0; i < 6; i++)
            {
                writer.Write(MakeEvent(message: "X" + i));
                // LastWriteTime に意味を持たせるため、書込みごとに少し待ち、
                // アーカイブされたファイルの mtime を明示的にずらす
                var archived = Path.Combine(_tempDir, $"cap.{i + 1}.log");
                if (System.IO.File.Exists(archived))
                {
                    System.IO.File.SetLastWriteTimeUtc(archived, DateTime.UtcNow.AddSeconds(i));
                }
            }
        }

        // 最大2件しか残らない
        var archives = Directory
            .GetFiles(_tempDir, "cap.*.log")
            .Where(f => !string.Equals(Path.GetFileName(f), "cap.log", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(2, archives.Count);
    }

    // ───────────── Rolling 番号付け ─────────────

    [Fact]
    public void RollingNumbering_ShiftsIndexOnEachArchive()
    {
        var path = Path.Combine(_tempDir, "roll.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 5,
            ArchiveNumbering = ArchiveNumbering.Rolling,
            MaxArchiveFiles = 3,
        };

        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(message: "first")); // → roll.0.log になるはず
            writer.Write(MakeEvent(message: "secnd")); // → 既存 roll.0.log を roll.1.log にして新しい roll.0.log
            writer.Write(MakeEvent(message: "third")); // 同様
        }

        var roll0 = Path.Combine(_tempDir, "roll.0.log");
        var roll1 = Path.Combine(_tempDir, "roll.1.log");
        var roll2 = Path.Combine(_tempDir, "roll.2.log");

        Assert.True(System.IO.File.Exists(roll0));
        Assert.True(System.IO.File.Exists(roll1));
        Assert.True(System.IO.File.Exists(roll2));

        // 最新 (third) が roll.0.log にある
        Assert.Contains("third", System.IO.File.ReadAllText(roll0));
    }

    // ───────────── Date 番号付け ─────────────

    [Fact]
    public void DateNumbering_AppendsDateToArchive()
    {
        var path = Path.Combine(_tempDir, "datearch.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 5,
            ArchiveNumbering = ArchiveNumbering.Date,
            ArchiveDateFormat = "yyyyMMdd",
            MaxArchiveFiles = 0,
        };

        var ts = new DateTime(2026, 4, 14, 12, 0, 0);
        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(timestamp: ts, message: "trigger archive!!"));
        }

        Assert.True(System.IO.File.Exists(Path.Combine(_tempDir, "datearch.20260414.log")));
    }

    // ───────────── ヘッダーとフッター ─────────────

    [Fact]
    public void Header_WrittenOnlyOnFreshFile()
    {
        var path = Path.Combine(_tempDir, "header.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            Header = "===HEADER===",
            ArchiveAboveSize = 0,
        };

        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(message: "line1"));
            writer.Write(MakeEvent(message: "line2"));
        }

        var lines = System.IO.File.ReadAllLines(path);
        Assert.Equal("===HEADER===", lines[0]);
        Assert.Equal("line1", lines[1]);
        Assert.Equal("line2", lines[2]);
    }

    [Fact]
    public void Footer_WrittenOnDispose()
    {
        var path = Path.Combine(_tempDir, "footer.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            Footer = "===FOOTER===",
            ArchiveAboveSize = 0,
        };

        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(message: "line1"));
        }

        var lines = System.IO.File.ReadAllLines(path);
        Assert.Equal("line1", lines[0]);
        Assert.Equal("===FOOTER===", lines[^1]);
    }

    // ───────────── KeepFileOpen ─────────────

    [Fact]
    public void KeepFileOpen_False_ClosesAfterEachWrite()
    {
        var path = Path.Combine(_tempDir, "noopen.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            KeepFileOpen = false,
            ArchiveAboveSize = 0,
        };

        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(message: "x"));
            // Write 後にファイルがクローズされていれば、別プロセス的に読める
            string content = System.IO.File.ReadAllText(path);
            Assert.Equal("x" + Environment.NewLine, content);
        }
    }

    // ───────────── Dispose 時のクローズ ─────────────

    [Fact]
    public void Dispose_ReleasesFileHandle()
    {
        var path = Path.Combine(_tempDir, "release.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        var writer = new FileTargetWriter(options);
        writer.Write(MakeEvent(message: "x"));
        writer.Dispose();

        // Dispose 後はハンドルが解放されているはず → 削除できる
        System.IO.File.Delete(path);
        Assert.False(System.IO.File.Exists(path));
    }

    [Fact]
    public void Write_AfterDispose_DoesNotThrow()
    {
        var path = Path.Combine(_tempDir, "afterdispose.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
        };

        var writer = new FileTargetWriter(options);
        writer.Write(MakeEvent(message: "x"));
        writer.Dispose();

        // 例外を投げない
        writer.Write(MakeEvent(message: "after"));
    }

    // ───────────── 時間境界アーカイブ (回帰) ─────────────

    [Fact]
    public void TimeBoundary_DateNumbering_NamesArchiveWithClosingPeriod()
    {
        // 回帰: ArchiveEvery=Day で 4/14 → 4/15 を跨ぐとき、
        // アーカイブ名は閉じる方の period (4/14) で付かなければならない。
        // 旧バグでは新しい event の timestamp (4/15) で命名され *.20260415.log になっていた。
        var path = Path.Combine(_tempDir, "boundary.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveEvery = ArchiveEvery.Day,
            ArchiveNumbering = ArchiveNumbering.Date,
            ArchiveDateFormat = "yyyyMMdd",
            MaxArchiveFiles = 0,
        };

        var day1 = new DateTime(2026, 4, 14, 23, 59, 30);
        var day2 = new DateTime(2026, 4, 15, 0, 0, 30);

        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(timestamp: day1, message: "yesterday"));
            writer.Write(MakeEvent(timestamp: day2, message: "today"));
        }

        var expectedArchive = Path.Combine(_tempDir, "boundary.20260414.log");
        var wrongArchive = Path.Combine(_tempDir, "boundary.20260415.log");

        Assert.True(System.IO.File.Exists(expectedArchive),
            $"閉じる period 名 ({Path.GetFileName(expectedArchive)}) でアーカイブされること");
        Assert.False(System.IO.File.Exists(wrongArchive),
            $"新 period 名 ({Path.GetFileName(wrongArchive)}) でアーカイブされてはいけない");
        Assert.Contains("yesterday", System.IO.File.ReadAllText(expectedArchive));
        Assert.Contains("today", System.IO.File.ReadAllText(path));
    }

    // ───────────── 日付テンプレ ArchiveFileName + 保持数 (回帰) ─────────────

    [Fact]
    public void MaxArchiveFiles_DatedArchiveFileName_SeesOldDates()
    {
        // 回帰: ArchiveFileName が ${shortdate} を含む場合、
        // ListArchives が現在日付で具象化した glob を作るため
        // 過去日付のアーカイブが retention の対象から外れていた。
        var path = Path.Combine(_tempDir, "dated.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 5,
            ArchiveNumbering = ArchiveNumbering.Sequence,
            ArchiveFileName = Path.Combine(_tempDir, "dated_${date:format=yyyyMMdd}.{#}.log"),
            MaxArchiveFiles = 1,
        };

        // 「昨日のアーカイブ」を手で配置する (mtime も古めにする)
        var oldArchive = Path.Combine(_tempDir, "dated_20260413.1.log");
        System.IO.File.WriteAllText(oldArchive, "old");
        System.IO.File.SetLastWriteTimeUtc(oldArchive, DateTime.UtcNow.AddDays(-1));

        var ts = new DateTime(2026, 4, 14, 12, 0, 0);
        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(timestamp: ts, message: "trigger archive!!"));
        }

        // 新しい dated_20260414.*.log のアーカイブが 1 件作られる
        var todays = Directory.GetFiles(_tempDir, "dated_20260414.*.log");
        Assert.Single(todays);

        // MaxArchiveFiles=1 なので、昨日のアーカイブは古い方として削除されていなければならない
        Assert.False(System.IO.File.Exists(oldArchive),
            "日付テンプレ違いの過去アーカイブも MaxArchiveFiles の対象になること");
    }

    // ───────────── 回帰: FileName テンプレ ev フィールド対応 ─────────────

    [Fact]
    public void FileName_LoggerToken_RoutesPerLogger()
    {
        // 回帰: FileName に ${logger} を入れた場合、ロガー名ごとに別ファイルへ出力されること。
        // (以前は LogEvent.ForPath(timestamp) で render していたため Logger が空文字に倒れて
        //  全イベントが「.log」のような同じ単一ファイルへ流れてしまっていた)
        // 注: パスセパレータ直後に "${...}" を書くと "\$" がリテラルエスケープと衝突するため、
        //     リテラルプレフィックスを挟む (実用的には NLog 例の "log_${logger}.log" がこれに該当)。
        var template = Path.Combine(_tempDir, "log_${logger}.log");
        var options = new FileTargetOptions
        {
            FileName = template,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(logger: "Foo", message: "foo-msg"));
            writer.Write(MakeEvent(logger: "Bar", message: "bar-msg"));
            writer.Write(MakeEvent(logger: "Foo", message: "foo-2nd"));
        }

        var fooPath = Path.Combine(_tempDir, "log_Foo.log");
        var barPath = Path.Combine(_tempDir, "log_Bar.log");
        Assert.True(System.IO.File.Exists(fooPath), "log_Foo.log が作成されていること");
        Assert.True(System.IO.File.Exists(barPath), "log_Bar.log が作成されていること");

        var fooContent = System.IO.File.ReadAllText(fooPath);
        var barContent = System.IO.File.ReadAllText(barPath);
        Assert.Contains("foo-msg", fooContent);
        Assert.Contains("foo-2nd", fooContent);
        Assert.DoesNotContain("bar-msg", fooContent);
        Assert.Contains("bar-msg", barContent);
        Assert.DoesNotContain("foo-msg", barContent);
    }

    // ───────────── 回帰: CleanupOldArchives 兄弟ファイル保護 ─────────────

    [Fact]
    public void CleanupOldArchives_DoesNotDeleteUnrelatedSiblingFiles()
    {
        // 回帰: ListArchives が `${baseName}.*${ext}` という素の glob だけで判定していたため、
        // `app.audit.log` のような無関係な兄弟ファイルが retention の巻き添えで削除されていた。
        var path = Path.Combine(_tempDir, "app.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 5,
            ArchiveNumbering = ArchiveNumbering.Sequence,
            MaxArchiveFiles = 1,
        };

        // アーカイブ命名規則に合致しない兄弟ファイル
        var sibling = Path.Combine(_tempDir, "app.audit.log");
        System.IO.File.WriteAllText(sibling, "audit data");

        using (var writer = new FileTargetWriter(options))
        {
            // 5 byte 超えるごとに rotation を発火させる
            writer.Write(MakeEvent(message: "0123456789"));
            writer.Write(MakeEvent(message: "0123456789"));
            writer.Write(MakeEvent(message: "0123456789"));
        }

        // 兄弟ファイルは絶対に残っていなければならない
        Assert.True(System.IO.File.Exists(sibling),
            "アーカイブ命名規則に合致しない兄弟ファイルは retention で消されてはいけない");
        Assert.Equal("audit data", System.IO.File.ReadAllText(sibling));
    }

    // ───────────── 回帰: Footer with KeepFileOpen=false ─────────────

    [Fact]
    public void Footer_KeepFileOpenFalse_StillEmittedOnDispose()
    {
        // 回帰: KeepFileOpen=false 経路では Write 直後に CloseStream(writeFooter:false) で
        // 閉じてしまうため、Dispose 時に footer が一切書かれていなかった。
        var path = Path.Combine(_tempDir, "foot.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            Footer = "===END===",
            KeepFileOpen = false,
            ArchiveAboveSize = 0,
        };

        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(message: "line1"));
            writer.Write(MakeEvent(message: "line2"));
        }

        Assert.True(System.IO.File.Exists(path));
        string content = System.IO.File.ReadAllText(path);
        Assert.Contains("line1", content);
        Assert.Contains("line2", content);
        Assert.Contains("===END===", content);
    }

    [Fact]
    public void Footer_KeepFileOpenFalse_EmittedOnArchive()
    {
        // 回帰: Archive を跨ぐ際にも footer は閉じる側のファイルに書かれていなければならない。
        var path = Path.Combine(_tempDir, "footarc.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            Footer = "===END===",
            KeepFileOpen = false,
            ArchiveAboveSize = 5,
            ArchiveNumbering = ArchiveNumbering.Sequence,
        };

        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(message: "0123456789")); // 10 文字 + LF → archive 発火
            writer.Write(MakeEvent(message: "second"));
        }

        // アーカイブされた最初のファイルに footer が含まれること
        var archive = Path.Combine(_tempDir, "footarc.1.log");
        Assert.True(System.IO.File.Exists(archive), $"アーカイブ {archive} が存在すること");
        string archiveContent = System.IO.File.ReadAllText(archive);
        Assert.Contains("0123456789", archiveContent);
        Assert.Contains("===END===", archiveContent);
    }

    // ───────────── Codex Round-3 回帰テスト ─────────────

    [Fact]
    public void ArchiveFileName_LoggerToken_RendersPerEvent()
    {
        // [P2] 回帰: ArchiveFileName に ${logger} 等の event 依存トークンが入っている場合、
        // LogEvent.ForPath(now) では Logger が空文字になり全ロガーで同じアーカイブ名に倒れてしまう。
        // 実 LogEvent (ev.Logger を含む) で展開しなければならない。
        var active = Path.Combine(_tempDir, "events.log");
        var archiveTemplate = Path.Combine(_tempDir, "archive_${logger}.{#}.log");
        var options = new FileTargetOptions
        {
            FileName = active,
            ArchiveFileName = archiveTemplate,
            Layout = "${message}",
            ArchiveAboveSize = 5,
            ArchiveNumbering = ArchiveNumbering.Sequence,
            KeepFileOpen = true,
        };

        using (var writer = new FileTargetWriter(options))
        {
            // 10 文字 + LF で ArchiveAboveSize=5 を超えて archive 発火
            writer.Write(MakeEvent(logger: "Foo", message: "0123456789"));
        }

        var expected = Path.Combine(_tempDir, "archive_Foo.1.log");
        Assert.True(
            System.IO.File.Exists(expected),
            $"ArchiveFileName の ${{logger}} が実イベントで展開されていない。期待: {expected}");
    }

    [Fact]
    public void ArchiveFileName_Rolling_HonorsTemplate()
    {
        // [P2] 回帰: ArchiveNumbering=Rolling のときに ArchiveFileName を設定しても
        // ResolveRollingArchive が完全に無視していた。テンプレート経路の {#} 差し替えを使うこと。
        var active = Path.Combine(_tempDir, "rolling.log");
        var archiveTemplate = Path.Combine(_tempDir, "rolled.{#}.bin");
        var options = new FileTargetOptions
        {
            FileName = active,
            ArchiveFileName = archiveTemplate,
            Layout = "${message}",
            ArchiveAboveSize = 5,
            ArchiveNumbering = ArchiveNumbering.Rolling,
            MaxArchiveFiles = 3,
            KeepFileOpen = true,
        };

        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(message: "0123456789")); // → rolled.0.bin
            writer.Write(MakeEvent(message: "abcdefghij")); // → rolled.0.bin 上書き、前は rolled.1.bin
        }

        var top = Path.Combine(_tempDir, "rolled.0.bin");
        var next = Path.Combine(_tempDir, "rolled.1.bin");
        Assert.True(System.IO.File.Exists(top), $"ArchiveFileName 経由の Rolling アーカイブ {top} が存在すること");
        Assert.True(System.IO.File.Exists(next), $"シフト後の {next} が存在すること");

        // 従来の fallback 命名 (rolling.0.log) に倒れていないこと
        Assert.False(
            System.IO.File.Exists(Path.Combine(_tempDir, "rolling.0.log")),
            "Rolling が ArchiveFileName を無視して baseName.0.ext にフォールバックしている");
    }

    [Fact]
    public void MaxArchiveFiles_DynamicFileName_PrunesOldDates()
    {
        // [P1] 回帰: FileName="app_${shortdate}.log" のような動的ファイル名で
        // 日次ロールが path 変更として起きる場合、ListArchives の glob が
        // activePath の baseName から作られるため過去日付のファイルが一切拾えず、
        // MaxArchiveFiles が機能せず無限に溜まってしまう。
        var fileTemplate = Path.Combine(_tempDir, "app_${shortdate}.log");
        var options = new FileTargetOptions
        {
            FileName = fileTemplate,
            Layout = "${message}",
            MaxArchiveFiles = 1,
            ArchiveNumbering = ArchiveNumbering.Date,
            KeepFileOpen = true,
        };

        // 事前に「3 日分の過去ファイル」を配置 (1 日ずつ古い mtime で)
        var day0 = Path.Combine(_tempDir, "app_2026-04-10.log");
        var day1 = Path.Combine(_tempDir, "app_2026-04-11.log");
        var day2 = Path.Combine(_tempDir, "app_2026-04-12.log");
        System.IO.File.WriteAllText(day0, "old-0");
        System.IO.File.WriteAllText(day1, "old-1");
        System.IO.File.WriteAllText(day2, "old-2");
        System.IO.File.SetLastWriteTimeUtc(day0, DateTime.UtcNow.AddDays(-4));
        System.IO.File.SetLastWriteTimeUtc(day1, DateTime.UtcNow.AddDays(-3));
        System.IO.File.SetLastWriteTimeUtc(day2, DateTime.UtcNow.AddDays(-2));

        // 別のファイル (アーカイブではない) も置いておき、誤って削除されないことを確認
        var siblingNoDigit = Path.Combine(_tempDir, "app_audit.log");
        System.IO.File.WriteAllText(siblingNoDigit, "sibling");
        System.IO.File.SetLastWriteTimeUtc(siblingNoDigit, DateTime.UtcNow.AddDays(-5));

        // 「昨日の書込み」→「今日の書込み」で path 変更が発生し、path 変更分岐から
        // CleanupOldArchives が走ることを期待する。
        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(
                timestamp: new DateTime(2026, 4, 13, 10, 0, 0),
                message: "yesterday"));
            writer.Write(MakeEvent(
                timestamp: new DateTime(2026, 4, 14, 10, 0, 0),
                message: "today"));
        }

        // MaxArchiveFiles=1 なので「今日 (active) + 1 件のアーカイブ」だけ残るはず。
        // アーカイブは最も新しい昨日 (2026-04-13) のファイルが残り、古い 10/11/12 は削除される。
        Assert.True(
            System.IO.File.Exists(Path.Combine(_tempDir, "app_2026-04-14.log")),
            "今日のファイルは残る");
        Assert.True(
            System.IO.File.Exists(Path.Combine(_tempDir, "app_2026-04-13.log")),
            "直近 1 件 (昨日) は retention に残る");
        Assert.False(System.IO.File.Exists(day0), $"{day0} は古すぎて削除されるはず");
        Assert.False(System.IO.File.Exists(day1), $"{day1} は古すぎて削除されるはず");
        Assert.False(System.IO.File.Exists(day2), $"{day2} は古すぎて削除されるはず");

        // 数字を含まない兄弟ファイルは IsDynamicFileNameCandidate で弾かれるため残る
        Assert.True(
            System.IO.File.Exists(siblingNoDigit),
            "数字を含まない兄弟ファイルは retention の巻き添えにしてはならない");
    }

    [Fact]
    public void Footer_KeepFileOpenFalse_PathChange_EmitsFooter()
    {
        // [P3] 回帰: KeepFileOpen=false で FileName に ${logger} などの動的トークンが入り、
        // path 変更が発生したとき、既に _stream は null なので CloseStream を呼んでも footer が
        // 書き出されない。path 変更分岐でも ReopenForFooter が必要。
        var fileTemplate = Path.Combine(_tempDir, "log_${logger}.log");
        var options = new FileTargetOptions
        {
            FileName = fileTemplate,
            Layout = "${message}",
            Footer = "===END===",
            KeepFileOpen = false,
            ArchiveAboveSize = 0,
        };

        using (var writer = new FileTargetWriter(options))
        {
            writer.Write(MakeEvent(logger: "Foo", message: "foo-line"));
            writer.Write(MakeEvent(logger: "Bar", message: "bar-line")); // path 変更
        }

        var fooPath = Path.Combine(_tempDir, "log_Foo.log");
        Assert.True(System.IO.File.Exists(fooPath), "Foo のファイルが存在すること");
        string fooContent = System.IO.File.ReadAllText(fooPath);
        Assert.Contains("foo-line", fooContent);
        Assert.Contains("===END===", fooContent);
    }
}
