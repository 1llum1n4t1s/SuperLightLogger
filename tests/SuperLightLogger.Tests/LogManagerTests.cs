using Microsoft.Extensions.Logging;
using SuperLightLogger.Tests.Helpers;
using Xunit;

namespace SuperLightLogger.Tests;

[Collection(LogManagerCollection.Name)]
public class LogManagerTests : IDisposable
{
    public LogManagerTests()
    {
        LogManager.Reset();
    }

    public void Dispose()
    {
        LogManager.Reset();
    }

    [Fact]
    public void GetLogger_WithType_ReturnsILog()
    {
        var factory = new FakeLoggerFactory();
        LogManager.Configure(factory);

        var log = LogManager.GetLogger(typeof(LogManagerTests));

        Assert.NotNull(log);
        Assert.True(factory.HasLogger(typeof(LogManagerTests).FullName!));
    }

    [Fact]
    public void GetLogger_WithString_ReturnsILog()
    {
        var factory = new FakeLoggerFactory();
        LogManager.Configure(factory);

        var log = LogManager.GetLogger("MyLogger");

        Assert.NotNull(log);
        Assert.True(factory.HasLogger("MyLogger"));
    }

    [Fact]
    public void GetLogger_RoutesLogToCorrectLevel()
    {
        var factory = new FakeLoggerFactory();
        LogManager.Configure(factory);

        var log = LogManager.GetLogger("Test");
        log.Info("動作確認");

        var fakeLogger = factory.GetLogger("Test");
        Assert.Single(fakeLogger.Entries);
        Assert.Equal(LogLevel.Information, fakeLogger.Entries[0].Level);
        Assert.Equal("動作確認", fakeLogger.Entries[0].Message);
    }

    [Fact]
    public void GetCurrentClassLogger_UsesCallerTypeName()
    {
        var factory = new FakeLoggerFactory();
        LogManager.Configure(factory);

        var log = LogManager.GetCurrentClassLogger();
        log.Debug("test");

        Assert.True(factory.HasLogger(typeof(LogManagerTests).FullName!));
    }

    [Fact]
    public void GetLogger_BeforeConfigure_UsesNullLoggerFactory()
    {
        // Configure未呼び出し → NullLoggerFactoryにフォールバック
        var log = LogManager.GetLogger("Fallback");
        // 例外が出ないことを確認
        log.Info("これは出力されない");
    }

    [Fact]
    public void Configure_WithBuilder_CreatesFactory()
    {
        LogManager.Configure(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var log = LogManager.GetLogger("BuilderTest");
        Assert.NotNull(log);
    }

    [Fact]
    public void Shutdown_DisposesFactory()
    {
        var factory = new FakeLoggerFactory();
        LogManager.Configure(factory);
        LogManager.Shutdown();

        Assert.True(factory.IsDisposed);
    }

    [Fact]
    public void Reset_DoesNotDisposeFactory()
    {
        var factory = new FakeLoggerFactory();
        LogManager.Configure(factory);
        LogManager.Reset();

        Assert.False(factory.IsDisposed);
    }

    [Fact]
    public void GetLogger_WithNullType_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => LogManager.GetLogger((Type)null!));
    }

    [Fact]
    public void GetLogger_WithNullString_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => LogManager.GetLogger((string)null!));
    }

    [Fact]
    public void Configure_WithNullFactory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => LogManager.Configure((ILoggerFactory)null!));
    }

    [Fact]
    public async Task ConcurrentGetLogger_DoesNotThrow()
    {
        var factory = new FakeLoggerFactory();
        LogManager.Configure(factory);

        var tasks = Enumerable.Range(0, 100)
            .Select(i => Task.Run(() =>
            {
                var log = LogManager.GetLogger($"Concurrent-{i}");
                log.Info($"メッセージ {i}");
            }))
            .ToArray();

        await Task.WhenAll(tasks);
    }
}
