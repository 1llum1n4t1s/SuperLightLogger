using System;
using System.Globalization;
using Microsoft.Extensions.Logging;
using SuperLightLogger;
using Xunit;

namespace SuperLightLogger.Tests.Targets;

/// <summary>
/// <see cref="LayoutRenderer"/> のテンプレート解析・描画テスト。
/// </summary>
public class LayoutRendererTests
{
    private static LogEvent MakeEvent(
        DateTime? timestamp = null,
        LogLevel level = LogLevel.Information,
        string logger = "MyApp.MyClass",
        string message = "hello",
        Exception? exception = null,
        int threadId = 42,
        string? threadName = null)
        => new LogEvent(
            timestamp ?? new DateTime(2026, 4, 14, 15, 30, 45, DateTimeKind.Local).AddTicks(1234),
            level,
            logger,
            message,
            exception,
            threadId,
            threadName);

    // ───────────── リテラル ─────────────

    [Fact]
    public void PlainText_RendersAsIs()
    {
        var r = new LayoutRenderer("こんにちは");
        Assert.Equal("こんにちは", r.Render(MakeEvent()));
    }

    [Fact]
    public void Empty_RendersEmpty()
    {
        var r = new LayoutRenderer(string.Empty);
        Assert.Equal(string.Empty, r.Render(MakeEvent()));
    }

    [Fact]
    public void Escape_BackslashDollar_RendersAsDollar()
    {
        // \$ は $ リテラルとして escape される (リテラル ${ を書きたい場合のため)
        var r = new LayoutRenderer(@"a\${level}b");
        Assert.Equal("a${level}b", r.Render(MakeEvent()));
    }

    [Fact]
    public void Escape_DoubleBackslash_RendersAsBackslash()
    {
        var r = new LayoutRenderer(@"a\\b");
        Assert.Equal(@"a\b", r.Render(MakeEvent()));
    }

    [Fact]
    public void WindowsPath_PreservesBackslashes()
    {
        // Windows パスのバックスラッシュ (\U, \s, \A 等) は escape ではなく literal として残る
        var r = new LayoutRenderer(@"C:\Users\szk\app\log.txt");
        Assert.Equal(@"C:\Users\szk\app\log.txt", r.Render(MakeEvent()));
    }

    // ───────────── レベル ─────────────

    [Theory]
    [InlineData(LogLevel.Trace, "Trace")]
    [InlineData(LogLevel.Debug, "Debug")]
    [InlineData(LogLevel.Information, "Info")]
    [InlineData(LogLevel.Warning, "Warn")]
    [InlineData(LogLevel.Error, "Error")]
    [InlineData(LogLevel.Critical, "Fatal")]
    public void Level_RendersExpectedString(LogLevel level, string expected)
    {
        var r = new LayoutRenderer("${level}");
        Assert.Equal(expected, r.Render(MakeEvent(level: level)));
    }

    [Fact]
    public void Level_Uppercase_True()
    {
        var r = new LayoutRenderer("${level:uppercase=true}");
        Assert.Equal("INFO", r.Render(MakeEvent(level: LogLevel.Information)));
    }

    [Fact]
    public void Level_Lowercase_True()
    {
        var r = new LayoutRenderer("${level:lowercase=true}");
        Assert.Equal("warn", r.Render(MakeEvent(level: LogLevel.Warning)));
    }

    [Fact]
    public void Level_Padding_Positive_RightAligns()
    {
        // NLog 互換: 正の padding は右寄せ (左に空白を詰める)
        var r = new LayoutRenderer("${level:padding=8}");
        Assert.Equal("    Info", r.Render(MakeEvent(level: LogLevel.Information)));
    }

    [Fact]
    public void Level_Padding_Negative_LeftAligns()
    {
        // NLog 互換: 負の padding は左寄せ (右に空白を詰める)
        var r = new LayoutRenderer("${level:padding=-8}");
        Assert.Equal("Info    ", r.Render(MakeEvent(level: LogLevel.Information)));
    }

    // ───────────── 日付 ─────────────

    [Fact]
    public void Date_DefaultFormat_IsLongDate()
    {
        var ts = new DateTime(2026, 4, 14, 15, 30, 45, DateTimeKind.Local).AddTicks(1234);
        var r = new LayoutRenderer("${date}");
        Assert.Equal(ts.ToString("yyyy-MM-dd HH:mm:ss.ffff", CultureInfo.InvariantCulture), r.Render(MakeEvent(timestamp: ts)));
    }

    [Fact]
    public void Date_CustomFormat_FromBody()
    {
        var ts = new DateTime(2026, 4, 14);
        var r = new LayoutRenderer("${date:format=yyyyMMdd}");
        Assert.Equal("20260414", r.Render(MakeEvent(timestamp: ts)));
    }

    [Fact]
    public void Date_FormatWithEscapedColon_Works()
    {
        var ts = new DateTime(2026, 4, 14, 1, 2, 3);
        var r = new LayoutRenderer(@"${date:format=HH\:mm\:ss}");
        Assert.Equal("01:02:03", r.Render(MakeEvent(timestamp: ts)));
    }

    [Fact]
    public void ShortDate_RendersYyyyMMdd()
    {
        var ts = new DateTime(2026, 4, 14, 15, 30, 45);
        var r = new LayoutRenderer("${shortdate}");
        Assert.Equal("2026-04-14", r.Render(MakeEvent(timestamp: ts)));
    }

    [Fact]
    public void LongDate_RendersFullPrecision()
    {
        var ts = new DateTime(2026, 4, 14, 15, 30, 45).AddTicks(1234);
        var r = new LayoutRenderer("${longdate}");
        Assert.Equal(ts.ToString("yyyy-MM-dd HH:mm:ss.ffff", CultureInfo.InvariantCulture), r.Render(MakeEvent(timestamp: ts)));
    }

    // ───────────── 標準フィールド ─────────────

    [Fact]
    public void Logger_RendersCategoryName()
    {
        var r = new LayoutRenderer("${logger}");
        Assert.Equal("MyApp.MyClass", r.Render(MakeEvent(logger: "MyApp.MyClass")));
    }

    [Fact]
    public void Message_RendersMessageText()
    {
        var r = new LayoutRenderer("${message}");
        Assert.Equal("テストメッセージ", r.Render(MakeEvent(message: "テストメッセージ")));
    }

    [Fact]
    public void ThreadId_RendersInteger()
    {
        var r = new LayoutRenderer("${threadid}");
        Assert.Equal("99", r.Render(MakeEvent(threadId: 99)));
    }

    [Fact]
    public void Newline_RendersEnvironmentNewLine()
    {
        var r = new LayoutRenderer("a${newline}b");
        Assert.Equal("a" + Environment.NewLine + "b", r.Render(MakeEvent()));
    }

    [Fact]
    public void MachineName_RendersEnvironmentMachineName()
    {
        var r = new LayoutRenderer("${machinename}");
        Assert.Equal(Environment.MachineName, r.Render(MakeEvent()));
    }

    // ───────────── 例外 ─────────────

    [Fact]
    public void Exception_NoException_RendersEmpty()
    {
        var r = new LayoutRenderer("${exception}");
        Assert.Equal(string.Empty, r.Render(MakeEvent(exception: null)));
    }

    [Fact]
    public void Exception_FormatMessage_RendersMessage()
    {
        var ex = new InvalidOperationException("boom");
        var r = new LayoutRenderer("${exception:format=message}");
        Assert.Equal("boom", r.Render(MakeEvent(exception: ex)));
    }

    [Fact]
    public void Exception_FormatType_RendersFullTypeName()
    {
        var ex = new InvalidOperationException("boom");
        var r = new LayoutRenderer("${exception:format=type}");
        Assert.Equal("System.InvalidOperationException", r.Render(MakeEvent(exception: ex)));
    }

    [Fact]
    public void Exception_FormatToString_RendersFullToString()
    {
        var ex = new InvalidOperationException("boom");
        var r = new LayoutRenderer("${exception:format=tostring}");
        var rendered = r.Render(MakeEvent(exception: ex));
        Assert.Contains("InvalidOperationException", rendered);
        Assert.Contains("boom", rendered);
    }

    // ───────────── onexception (ネスト) ─────────────

    [Fact]
    public void OnException_NoException_OmitsBody()
    {
        var r = new LayoutRenderer("msg${onexception:${newline}${exception:format=message}}");
        Assert.Equal("msg", r.Render(MakeEvent(exception: null)));
    }

    [Fact]
    public void OnException_WithException_RendersBody()
    {
        var ex = new InvalidOperationException("boom");
        var r = new LayoutRenderer("msg${onexception:${newline}${exception:format=message}}");
        Assert.Equal("msg" + Environment.NewLine + "boom", r.Render(MakeEvent(exception: ex)));
    }

    // ───────────── 統合: NLog 互換デフォルトレイアウト ─────────────

    [Fact]
    public void DefaultLayout_NoException_FormatMatches()
    {
        var ts = new DateTime(2026, 4, 14, 15, 30, 45).AddTicks(1234);
        var template = @"${date:format=yyyy-MM-dd HH\:mm\:ss.ffff} [${level:uppercase=true}] [${threadid}] ${message}${onexception:${newline}${exception:format=tostring}}";
        var r = new LayoutRenderer(template);
        var output = r.Render(MakeEvent(timestamp: ts, level: LogLevel.Information, message: "ハロー", threadId: 7));

        Assert.Equal("2026-04-14 15:30:45.0001 [INFO] [7] ハロー", output);
    }

    [Fact]
    public void DefaultLayout_WithException_AppendsStack()
    {
        var ts = new DateTime(2026, 4, 14, 15, 30, 45);
        var ex = new InvalidOperationException("失敗");
        var template = @"${date:format=yyyy-MM-dd HH\:mm\:ss.ffff} [${level:uppercase=true}] [${threadid}] ${message}${onexception:${newline}${exception:format=tostring}}";
        var r = new LayoutRenderer(template);
        var output = r.Render(MakeEvent(timestamp: ts, level: LogLevel.Error, message: "エラー", exception: ex, threadId: 7));

        Assert.StartsWith("2026-04-14 15:30:45.0000 [ERROR] [7] エラー" + Environment.NewLine, output);
        Assert.Contains("InvalidOperationException", output);
        Assert.Contains("失敗", output);
    }

    // ───────────── 未知レンダラ ─────────────

    [Fact]
    public void UnknownRenderer_LiteralizedAsIs()
    {
        var r = new LayoutRenderer("${unknownthing:foo=bar}");
        Assert.Equal("${unknownthing:foo=bar}", r.Render(MakeEvent()));
    }
}
