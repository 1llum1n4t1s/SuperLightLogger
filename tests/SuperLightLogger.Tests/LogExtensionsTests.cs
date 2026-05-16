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

    // ---- ILog 第三者実装 (Log を継承しない) のフォールバック経路の回帰テスト (#B3-002) ----

    [Fact]
    public void InfoStructured_OnCustomILogImpl_WithMelTemplate_DoesNotThrow()
    {
        // ILog の自前実装に対しては {UserId} 形式の MEL テンプレートを直接 string.Format に
        // 渡すと FormatException を起こす経路があった。フォールバック try/catch で吸収して
        // テンプレートと引数を可読フォーマットで Info() に転送するように改修済み。
        var custom = new CapturingILog();
        custom.InfoStructured("ユーザー {UserId} がログインしました", "tanaka");

        Assert.Equal(LogLevel.Information, custom.CapturedLevel);
        Assert.NotNull(custom.CapturedMessage);
        // 名前付きテンプレ + 引数の両方が結果に含まれていれば、フォールバックフォーマットが機能している
        Assert.Contains("UserId", custom.CapturedMessage!);
        Assert.Contains("tanaka", custom.CapturedMessage!);
    }

    [Fact]
    public void ErrorStructured_OnCustomILogImpl_WithMelTemplate_DoesNotThrowAndCapturesException()
    {
        var custom = new CapturingILog();
        var ex = new InvalidOperationException("broken");
        custom.ErrorStructured(ex, "処理 {Task} で失敗", "Import");

        Assert.Equal(LogLevel.Error, custom.CapturedLevel);
        Assert.Same(ex, custom.CapturedException);
        Assert.NotNull(custom.CapturedMessage);
        Assert.Contains("Import", custom.CapturedMessage!);
    }

    /// <summary>
    /// <see cref="Log"/> を継承しない自前 <see cref="ILog"/> 実装。
    /// <see cref="LogExtensions.LogStructured"/> のフォールバック経路 (string.Format → log.Trace/Debug/Info/Warn/Error/Fatal)
    /// を踏むときに渡される引数を捕捉して、テストで assertion 可能にする。
    /// </summary>
    private sealed class CapturingILog : ILog
    {
        public LogLevel CapturedLevel;
        public string? CapturedMessage;
        public Exception? CapturedException;

        public bool IsTraceEnabled => true;
        public bool IsDebugEnabled => true;
        public bool IsInfoEnabled => true;
        public bool IsWarnEnabled => true;
        public bool IsErrorEnabled => true;
        public bool IsFatalEnabled => true;

        // LogStructured フォールバックで呼ばれる 6 メソッド (object?, Exception?)
        public void Trace(object? message, Exception? exception) => Capture(LogLevel.Trace, message, exception);
        public void Debug(object? message, Exception? exception) => Capture(LogLevel.Debug, message, exception);
        public void Info(object? message, Exception? exception) => Capture(LogLevel.Information, message, exception);
        public void Warn(object? message, Exception? exception) => Capture(LogLevel.Warning, message, exception);
        public void Error(object? message, Exception? exception) => Capture(LogLevel.Error, message, exception);
        public void Fatal(object? message, Exception? exception) => Capture(LogLevel.Critical, message, exception);

        // 以下、ILog の残りメンバ (フォールバック経路では呼ばれない前提)
        public void Trace(object? message) => throw new NotImplementedException();
        public void TraceFormat(string format, params object?[] args) => throw new NotImplementedException();
        public void TraceFormat(string format, object? arg0) => throw new NotImplementedException();
        public void TraceFormat(string format, object? arg0, object? arg1) => throw new NotImplementedException();
        public void TraceFormat(string format, object? arg0, object? arg1, object? arg2) => throw new NotImplementedException();
        public void TraceFormat(IFormatProvider? provider, string format, params object?[] args) => throw new NotImplementedException();

        public void Debug(object? message) => throw new NotImplementedException();
        public void DebugFormat(string format, params object?[] args) => throw new NotImplementedException();
        public void DebugFormat(string format, object? arg0) => throw new NotImplementedException();
        public void DebugFormat(string format, object? arg0, object? arg1) => throw new NotImplementedException();
        public void DebugFormat(string format, object? arg0, object? arg1, object? arg2) => throw new NotImplementedException();
        public void DebugFormat(IFormatProvider? provider, string format, params object?[] args) => throw new NotImplementedException();

        public void Info(object? message) => throw new NotImplementedException();
        public void InfoFormat(string format, params object?[] args) => throw new NotImplementedException();
        public void InfoFormat(string format, object? arg0) => throw new NotImplementedException();
        public void InfoFormat(string format, object? arg0, object? arg1) => throw new NotImplementedException();
        public void InfoFormat(string format, object? arg0, object? arg1, object? arg2) => throw new NotImplementedException();
        public void InfoFormat(IFormatProvider? provider, string format, params object?[] args) => throw new NotImplementedException();

        public void Warn(object? message) => throw new NotImplementedException();
        public void WarnFormat(string format, params object?[] args) => throw new NotImplementedException();
        public void WarnFormat(string format, object? arg0) => throw new NotImplementedException();
        public void WarnFormat(string format, object? arg0, object? arg1) => throw new NotImplementedException();
        public void WarnFormat(string format, object? arg0, object? arg1, object? arg2) => throw new NotImplementedException();
        public void WarnFormat(IFormatProvider? provider, string format, params object?[] args) => throw new NotImplementedException();

        public void Error(object? message) => throw new NotImplementedException();
        public void ErrorFormat(string format, params object?[] args) => throw new NotImplementedException();
        public void ErrorFormat(string format, object? arg0) => throw new NotImplementedException();
        public void ErrorFormat(string format, object? arg0, object? arg1) => throw new NotImplementedException();
        public void ErrorFormat(string format, object? arg0, object? arg1, object? arg2) => throw new NotImplementedException();
        public void ErrorFormat(IFormatProvider? provider, string format, params object?[] args) => throw new NotImplementedException();

        public void Fatal(object? message) => throw new NotImplementedException();
        public void FatalFormat(string format, params object?[] args) => throw new NotImplementedException();
        public void FatalFormat(string format, object? arg0) => throw new NotImplementedException();
        public void FatalFormat(string format, object? arg0, object? arg1) => throw new NotImplementedException();
        public void FatalFormat(string format, object? arg0, object? arg1, object? arg2) => throw new NotImplementedException();
        public void FatalFormat(IFormatProvider? provider, string format, params object?[] args) => throw new NotImplementedException();

        private void Capture(LogLevel level, object? message, Exception? exception)
        {
            CapturedLevel = level;
            CapturedMessage = message?.ToString();
            CapturedException = exception;
        }
    }
}
