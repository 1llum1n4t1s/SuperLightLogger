using Microsoft.Extensions.Logging;
using SuperLightLogger.Tests.Helpers;
using Xunit;

namespace SuperLightLogger.Tests;

public class LogTests
{
    private readonly FakeLogger _fakeLogger = new();
    private readonly ILog _log;

    public LogTests()
    {
        _log = new Log(_fakeLogger);
    }

    #region レベルマッピングテスト

    [Fact]
    public void Trace_MapsToLogLevelTrace()
    {
        _log.Trace("test");
        Assert.Single(_fakeLogger.Entries);
        Assert.Equal(LogLevel.Trace, _fakeLogger.Entries[0].Level);
    }

    [Fact]
    public void Debug_MapsToLogLevelDebug()
    {
        _log.Debug("test");
        Assert.Single(_fakeLogger.Entries);
        Assert.Equal(LogLevel.Debug, _fakeLogger.Entries[0].Level);
    }

    [Fact]
    public void Info_MapsToLogLevelInformation()
    {
        _log.Info("test");
        Assert.Single(_fakeLogger.Entries);
        Assert.Equal(LogLevel.Information, _fakeLogger.Entries[0].Level);
    }

    [Fact]
    public void Warn_MapsToLogLevelWarning()
    {
        _log.Warn("test");
        Assert.Single(_fakeLogger.Entries);
        Assert.Equal(LogLevel.Warning, _fakeLogger.Entries[0].Level);
    }

    [Fact]
    public void Error_MapsToLogLevelError()
    {
        _log.Error("test");
        Assert.Single(_fakeLogger.Entries);
        Assert.Equal(LogLevel.Error, _fakeLogger.Entries[0].Level);
    }

    [Fact]
    public void Fatal_MapsToLogLevelCritical()
    {
        _log.Fatal("test");
        Assert.Single(_fakeLogger.Entries);
        Assert.Equal(LogLevel.Critical, _fakeLogger.Entries[0].Level);
    }

    #endregion

    #region メッセージとExceptionテスト

    [Fact]
    public void Debug_WithMessage_LogsMessage()
    {
        _log.Debug("hello world");
        Assert.Equal("hello world", _fakeLogger.Entries[0].Message);
    }

    [Fact]
    public void Debug_WithObjectMessage_CallsToString()
    {
        _log.Debug(42);
        Assert.Equal("42", _fakeLogger.Entries[0].Message);
    }

    [Fact]
    public void Debug_WithNullMessage_DoesNotThrow()
    {
        _log.Debug(null);
        Assert.Single(_fakeLogger.Entries);
    }

    [Fact]
    public void Error_WithException_LogsException()
    {
        var ex = new InvalidOperationException("boom");
        _log.Error("エラーだよ", ex);

        Assert.Single(_fakeLogger.Entries);
        Assert.Equal("エラーだよ", _fakeLogger.Entries[0].Message);
        Assert.Same(ex, _fakeLogger.Entries[0].Exception);
    }

    #endregion

    #region Format系テスト

    [Fact]
    public void InfoFormat_WithParams_FormatsCorrectly()
    {
        _log.InfoFormat("ユーザー{0}がログインしました", "太郎");
        Assert.Equal("ユーザー太郎がログインしました", _fakeLogger.Entries[0].Message);
    }

    [Fact]
    public void DebugFormat_WithOneArg_FormatsCorrectly()
    {
        _log.DebugFormat("value={0}", 100);
        Assert.Equal("value=100", _fakeLogger.Entries[0].Message);
    }

    [Fact]
    public void WarnFormat_WithTwoArgs_FormatsCorrectly()
    {
        _log.WarnFormat("{0}/{1}", "a", "b");
        Assert.Equal("a/b", _fakeLogger.Entries[0].Message);
    }

    [Fact]
    public void ErrorFormat_WithThreeArgs_FormatsCorrectly()
    {
        _log.ErrorFormat("{0}-{1}-{2}", 1, 2, 3);
        Assert.Equal("1-2-3", _fakeLogger.Entries[0].Message);
    }

    [Fact]
    public void Format_WithoutProvider_UsesInvariantCultureRegardlessOfCurrentCulture()
    {
        // 回帰: provider 未指定の *Format が現在カルチャで書式化され、log4net (Invariant) と出力が食い違っていた。
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            _log.InfoFormat("{0}", 1.5);                     // 1 引数版
            _log.InfoFormat("{0}", (object?)2.5);            // 1 引数版 (明示)
            _log.InfoFormat("{0}/{1}", 3.5, 4.5);            // 2 引数版
            _log.InfoFormat("{0}/{1}/{2}", 5.5, 6.5, 7.5);   // 3 引数版
            _log.InfoFormat("{0}{1}{2}{3}", 1.5, 2.5, 3.5, 4.5); // 4 引数 = params 版
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }

        Assert.Equal("1.5", _fakeLogger.Entries[0].Message);
        Assert.Equal("2.5", _fakeLogger.Entries[1].Message);
        Assert.Equal("3.5/4.5", _fakeLogger.Entries[2].Message);
        Assert.Equal("5.5/6.5/7.5", _fakeLogger.Entries[3].Message);
        Assert.Equal("1.52.53.54.5", _fakeLogger.Entries[4].Message);
    }

    [Fact]
    public void Format_WithExplicitNullProvider_KeepsCurrentCulture()
    {
        // provider 版へ明示的に null を渡した場合は string.Format と同じく現在カルチャのまま (従来互換)。
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            _log.InfoFormat((IFormatProvider?)null, "{0}", 1.5);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }

        Assert.Equal("1,5", _fakeLogger.Entries[0].Message);
    }

    [Fact]
    public void FatalFormat_WithProvider_FormatsCorrectly()
    {
        _log.FatalFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:N2}", 1234.5);
        Assert.Equal("1,234.50", _fakeLogger.Entries[0].Message);
    }

    #endregion

    #region IsEnabled テスト

    [Fact]
    public void IsDebugEnabled_WhenMinimumIsInfo_ReturnsFalse()
    {
        var logger = new FakeLogger(LogLevel.Information);
        var log = new Log(logger);
        Assert.False(log.IsDebugEnabled);
        Assert.True(log.IsInfoEnabled);
    }

    [Fact]
    public void Debug_WhenDisabled_SkipsLogging()
    {
        var logger = new FakeLogger(LogLevel.Warning);
        var log = new Log(logger);

        log.Debug("スキップされるはず");
        Assert.Empty(logger.Entries);
    }

    #endregion
}
