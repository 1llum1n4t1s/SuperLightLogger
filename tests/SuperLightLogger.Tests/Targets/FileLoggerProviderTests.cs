using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuperLightLogger;
using Xunit;

namespace SuperLightLogger.Tests.Targets;

/// <summary>
/// <see cref="FileLoggerProvider"/> および <see cref="FileLoggerExtensions"/> の
/// MEL 統合エンドツーエンドテスト。
/// </summary>
public class FileLoggerProviderTests : IDisposable
{
    private readonly string _tempDir;

    public FileLoggerProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SuperLightLoggerProviderTests_" + Guid.NewGuid().ToString("N"));
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
            /* ignored */
        }
    }

    private LoggerFactory CreateFactory(FileTargetOptions options)
    {
        var factory = new LoggerFactory();
        factory.AddProvider(new FileLoggerProvider(options));
        return factory;
    }

    // ───────────── 基本: M.E.L → ファイル ─────────────

    [Fact]
    public void Logger_WritesToFile_ViaMicrosoftExtensionsLogging()
    {
        var path = Path.Combine(_tempDir, "mel.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${level}|${logger}|${message}",
            ArchiveAboveSize = 0,
        };

        using (var factory = CreateFactory(options))
        {
            var logger = factory.CreateLogger("Test.Category");
            logger.LogInformation("hello world");
        }

        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("Info|Test.Category|hello world", content);
    }

    [Fact]
    public void Logger_RespectsMinLevel()
    {
        var path = Path.Combine(_tempDir, "minlevel.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${level}|${message}",
            MinLevel = LogLevel.Warning,
            ArchiveAboveSize = 0,
        };

        using (var factory = CreateFactory(options))
        {
            var logger = factory.CreateLogger("Test");
            logger.LogTrace("trace");
            logger.LogDebug("debug");
            logger.LogInformation("info");
            logger.LogWarning("warn");
            logger.LogError("error");
        }

        var lines = System.IO.File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("Warn|warn", lines[0]);
        Assert.Contains("Error|error", lines[1]);
    }

    [Fact]
    public void Logger_LogException_RendersStackTraceViaOnException()
    {
        var path = Path.Combine(_tempDir, "ex.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            ArchiveAboveSize = 0,
        };

        using (var factory = CreateFactory(options))
        {
            var logger = factory.CreateLogger("Cat");
            try
            {
                throw new InvalidOperationException("やばい");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "失敗");
            }
        }

        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("[ERROR]", content);
        Assert.Contains("失敗", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("やばい", content);
    }

    // ───────────── 同一カテゴリで Logger が再利用される ─────────────

    [Fact]
    public void CreateLogger_ReturnsSameInstanceForSameCategory()
    {
        var path = Path.Combine(_tempDir, "cache.log");
        var options = new FileTargetOptions { FileName = path, ArchiveAboveSize = 0 };

        using (var provider = new FileLoggerProvider(options))
        {
            var a = provider.CreateLogger("foo");
            var b = provider.CreateLogger("foo");
            var c = provider.CreateLogger("bar");
            Assert.Same(a, b);
            Assert.NotSame(a, c);
        }
    }

    // ───────────── 並行書込み ─────────────

    [Fact]
    public void ConcurrentLoggers_AllLinesWritten()
    {
        var path = Path.Combine(_tempDir, "concurrent.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        const int threadCount = 8;
        const int perThread = 50;

        using (var factory = CreateFactory(options))
        {
            Parallel.For(0, threadCount, i =>
            {
                var logger = factory.CreateLogger("Thread" + i);
                for (int j = 0; j < perThread; j++)
                {
                    logger.LogInformation("T{Thread}-N{N}", i, j);
                }
            });
        }

        var lines = System.IO.File.ReadAllLines(path);
        Assert.Equal(threadCount * perThread, lines.Length);
    }

    // ───────────── 拡張メソッド ─────────────

    [Fact]
    public void AddSuperLightFile_WithAction_RegistersProvider()
    {
        var path = Path.Combine(_tempDir, "ext.log");

        using (var factory = LoggerFactory.Create(builder =>
        {
            builder.AddSuperLightFile(opt =>
            {
                opt.FileName = path;
                opt.Layout = "${message}";
                opt.ArchiveAboveSize = 0;
            });
        }))
        {
            var logger = factory.CreateLogger("X");
            logger.LogInformation("via extension");
        }

        Assert.Contains("via extension", System.IO.File.ReadAllText(path));
    }

    [Fact]
    public void AddSuperLightFile_WithOptions_RegistersProvider()
    {
        var path = Path.Combine(_tempDir, "ext2.log");
        var options = new FileTargetOptions
        {
            FileName = path,
            Layout = "${message}",
            ArchiveAboveSize = 0,
        };

        using (var factory = LoggerFactory.Create(builder => builder.AddSuperLightFile(options)))
        {
            factory.CreateLogger("X").LogInformation("hi");
        }

        Assert.Contains("hi", System.IO.File.ReadAllText(path));
    }

    [Fact]
    public void AddSuperLightFile_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FileLoggerExtensions.AddSuperLightFile(null!, (Action<FileTargetOptions>)(_ => { })));
    }

    [Fact]
    public void AddSuperLightFile_NullConfigure_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            LoggerFactory.Create(builder =>
                builder.AddSuperLightFile((Action<FileTargetOptions>)null!)));
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FileLoggerProvider(null!));
    }

    // ───────────── 改良: 文字列レベル設定 / 簡易オーバーロード ─────────────

    [Fact]
    public void MinLevelName_Setter_UpdatesMinLevel()
    {
        var opts = new FileTargetOptions();
        opts.MinLevelName = "Warn";
        Assert.Equal(LogLevel.Warning, opts.MinLevel);

        opts.MinLevelName = "Fatal";
        Assert.Equal(LogLevel.Critical, opts.MinLevel);
    }

    [Fact]
    public void MinLevelName_Getter_MirrorsMinLevel()
    {
        var opts = new FileTargetOptions { MinLevel = LogLevel.Information };
        Assert.Equal("Information", opts.MinLevelName);
    }

    [Fact]
    public void MinLevelName_UnknownValue_Throws()
    {
        var opts = new FileTargetOptions();
        Assert.Throws<ArgumentException>(() => opts.MinLevelName = "verbose");
    }

    [Fact]
    public void AddSuperLightFile_ByFileNameOnly_WritesAndUsesDefaults()
    {
        var path = Path.Combine(_tempDir, "shortcut.log");
        using (var factory = LoggerFactory.Create(b => b.AddSuperLightFile(path)))
        {
            factory.CreateLogger("Cat").LogInformation("shortcut works");
        }
        Assert.True(System.IO.File.Exists(path));
        Assert.Contains("shortcut works", System.IO.File.ReadAllText(path));
    }

    [Fact]
    public void AddSuperLightFile_FileNameOverload_NullFileName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            LoggerFactory.Create(b => b.AddSuperLightFile((string)null!)));
    }

    [Fact]
    public void AddSuperLightFile_FileNameOverload_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FileLoggerExtensions.AddSuperLightFile(null!, "any.log"));
    }
}
