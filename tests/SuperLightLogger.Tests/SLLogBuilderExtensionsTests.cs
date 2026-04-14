using System;
using Microsoft.Extensions.Logging;
using SuperLightLogger.Tests.Helpers;
using Xunit;

namespace SuperLightLogger.Tests;

/// <summary>
/// <see cref="SLLogBuilderExtensions.SetMinimumLevel(ILoggingBuilder, string)"/> が
/// MEL の LogLevel 依存なしでミニマムレベルを正しく設定できるかを検証する。
/// </summary>
[Collection(LogManagerCollection.Name)]
public class SLLogBuilderExtensionsTests : IDisposable
{
    public SLLogBuilderExtensionsTests()
    {
        LogManager.Reset();
    }

    public void Dispose()
    {
        LogManager.Reset();
    }

    [Fact]
    public void SetMinimumLevelString_InfoFiltersOutDebug()
    {
        // "Info" を SuperLightLogger の string オーバーロードで設定し、
        // Debug は握りつぶされ Info は通ることを確認
        var captured = new FakeLoggerProvider();
        LogManager.Configure(b =>
        {
            b.SetMinimumLevel("Info");       // ← MEL の using なし
            b.AddProvider(captured);
        });

        var log = LogManager.GetLogger("test");
        log.Debug("drop me");
        log.Info("keep me");

        Assert.DoesNotContain(captured.Events, e => e.Message == "drop me");
        Assert.Contains(captured.Events, e => e.Message == "keep me");
    }

    [Theory]
    [InlineData("Trace")]
    [InlineData("Debug")]
    [InlineData("Info")]
    [InlineData("Warn")]
    [InlineData("Error")]
    [InlineData("Fatal")]
    [InlineData("None")]
    public void SetMinimumLevelString_AcceptsAllCanonicalNames(string level)
    {
        // ビルダーが例外を投げずに値を受理できることだけ検証 (副作用は別テストで確認)
        var builder = LoggerFactory.Create(b => b.SetMinimumLevel(level));
        builder.Dispose();
    }

    [Fact]
    public void SetMinimumLevelString_UnknownLevelThrows()
    {
        Assert.Throws<ArgumentException>(() =>
            LoggerFactory.Create(b => b.SetMinimumLevel("verbose")));
    }

    [Fact]
    public void SetMinimumLevelString_NullLevelThrows()
    {
        Assert.Throws<ArgumentNullException>(() =>
            LoggerFactory.Create(b => b.SetMinimumLevel(null!)));
    }

    /// <summary>
    /// テスト用の <see cref="ILoggerProvider"/> — 受け取ったイベントを記録するだけ。
    /// </summary>
    private sealed class FakeLoggerProvider : ILoggerProvider
    {
        public readonly System.Collections.Generic.List<(LogLevel Level, string Message)> Events = new();

        public ILogger CreateLogger(string categoryName) => new ListLogger(Events);

        public void Dispose() { }

        private sealed class ListLogger : ILogger
        {
            private readonly System.Collections.Generic.List<(LogLevel, string)> _events;
            public ListLogger(System.Collections.Generic.List<(LogLevel, string)> events) { _events = events; }
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> fmt)
            {
                _events.Add((level, fmt(state, ex)));
            }
        }

        public System.Collections.Generic.IEnumerable<(LogLevel Level, string Message)> EventsView => Events;
    }
}
