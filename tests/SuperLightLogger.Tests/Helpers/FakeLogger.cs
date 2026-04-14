using Microsoft.Extensions.Logging;

namespace SuperLightLogger.Tests.Helpers;

/// <summary>
/// テスト用のILogger実装。ログエントリを記録する。
/// </summary>
internal sealed class FakeLogger : ILogger
{
    private readonly LogLevel _minimumLevel;

    public List<LogEntry> Entries { get; } = new();

    public FakeLogger(LogLevel minimumLevel = LogLevel.Trace)
    {
        _minimumLevel = minimumLevel;
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimumLevel;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
    }
}

internal record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);
