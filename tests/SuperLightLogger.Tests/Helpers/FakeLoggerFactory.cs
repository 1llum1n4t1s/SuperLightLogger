using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SuperLightLogger.Tests.Helpers;

/// <summary>
/// テスト用のILoggerFactory実装。FakeLoggerを生成・保持する。
/// </summary>
internal sealed class FakeLoggerFactory : ILoggerFactory
{
    private readonly LogLevel _minimumLevel;
    private readonly ConcurrentDictionary<string, FakeLogger> _loggers = new();

    public bool IsDisposed { get; private set; }

    public FakeLoggerFactory(LogLevel minimumLevel = LogLevel.Trace)
    {
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, _ => new FakeLogger(_minimumLevel));

    public FakeLogger GetLogger(string categoryName) => _loggers[categoryName];

    public bool HasLogger(string categoryName) => _loggers.ContainsKey(categoryName);

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
