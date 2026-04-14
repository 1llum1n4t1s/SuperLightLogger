using Microsoft.Extensions.Logging;
using SuperLightLogger.Tests.Helpers;
using Xunit;

namespace SuperLightLogger.Tests;

public class LogExtensionsTests
{
    private readonly FakeLogger _fakeLogger = new();
    private readonly ILog _log;

    public LogExtensionsTests()
    {
        _log = new Log(_fakeLogger);
    }

    [Fact]
    public void InfoStructured_LogsWithTemplate()
    {
        _log.InfoStructured("ユーザー {UserId} がログインしました", "user-123");

        Assert.Single(_fakeLogger.Entries);
        Assert.Equal(LogLevel.Information, _fakeLogger.Entries[0].Level);
        Assert.Contains("user-123", _fakeLogger.Entries[0].Message);
    }

    [Fact]
    public void ErrorStructured_WithException_LogsExceptionAndMessage()
    {
        var ex = new InvalidOperationException("broken");
        _log.ErrorStructured(ex, "処理 {TaskName} で失敗", "DataImport");

        Assert.Single(_fakeLogger.Entries);
        Assert.Equal(LogLevel.Error, _fakeLogger.Entries[0].Level);
        Assert.Same(ex, _fakeLogger.Entries[0].Exception);
        Assert.Contains("DataImport", _fakeLogger.Entries[0].Message);
    }

    [Fact]
    public void DebugStructured_WhenDisabled_SkipsLogging()
    {
        var logger = new FakeLogger(LogLevel.Warning);
        var log = new Log(logger);

        log.DebugStructured("スキップ {Key}", "value");
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void FatalStructured_MapsToLogLevelCritical()
    {
        _log.FatalStructured("システムダウン {Reason}", "OOM");

        Assert.Single(_fakeLogger.Entries);
        Assert.Equal(LogLevel.Critical, _fakeLogger.Entries[0].Level);
    }

    [Fact]
    public void TraceStructured_MapsToLogLevelTrace()
    {
        _log.TraceStructured("詳細 {Detail}", "abc");

        Assert.Single(_fakeLogger.Entries);
        Assert.Equal(LogLevel.Trace, _fakeLogger.Entries[0].Level);
    }

    [Fact]
    public void WarnStructured_MapsToLogLevelWarning()
    {
        _log.WarnStructured("警告 {Code}", 404);

        Assert.Single(_fakeLogger.Entries);
        Assert.Equal(LogLevel.Warning, _fakeLogger.Entries[0].Level);
    }
}
