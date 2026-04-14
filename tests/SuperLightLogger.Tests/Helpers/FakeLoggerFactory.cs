using Microsoft.Extensions.Logging;

namespace SuperLightLogger.Tests.Helpers;

/// <summary>
/// テスト用のILoggerFactory実装。FakeLoggerを生成・保持する。
/// </summary>
internal sealed class FakeLoggerFactory : ILoggerFactory
{
    private readonly LogLevel _minimumLevel;
    private readonly Dictionary<string, FakeLogger> _loggers = new();

    public bool IsDisposed { get; private set; }

    public FakeLoggerFactory(LogLevel minimumLevel = LogLevel.Trace)
    {
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName)
    {
        if (!_loggers.TryGetValue(categoryName, out var logger))
        {
            logger = new FakeLogger(_minimumLevel);
            _loggers[categoryName] = logger;
        }
        return logger;
    }

    public FakeLogger GetLogger(string categoryName) => _loggers[categoryName];

    public bool HasLogger(string categoryName) => _loggers.ContainsKey(categoryName);

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
