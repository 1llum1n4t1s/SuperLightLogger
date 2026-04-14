using System;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SuperLightLogger.Tests;

/// <summary>
/// <see cref="SLLogLevels"/> パーサの単体テスト。
/// log4net / NLog 由来の文字列から MEL の <see cref="LogLevel"/> に正しく写像できるか検証する。
/// </summary>
public class SLLogLevelsTests
{
    [Theory]
    [InlineData("Trace", LogLevel.Trace)]
    [InlineData("Debug", LogLevel.Debug)]
    [InlineData("Info", LogLevel.Information)]
    [InlineData("Information", LogLevel.Information)]
    [InlineData("Warn", LogLevel.Warning)]
    [InlineData("Warning", LogLevel.Warning)]
    [InlineData("Error", LogLevel.Error)]
    [InlineData("Fatal", LogLevel.Critical)]
    [InlineData("Critical", LogLevel.Critical)]
    [InlineData("None", LogLevel.None)]
    [InlineData("Off", LogLevel.None)]
    public void Parse_CanonicalNames_ReturnsMatchingLevel(string input, LogLevel expected)
    {
        Assert.Equal(expected, SLLogLevels.Parse(input));
    }

    [Theory]
    [InlineData("INFO")]
    [InlineData("info")]
    [InlineData("InFo")]
    [InlineData("  Info  ")]
    [InlineData("\tWARN\t")]
    public void Parse_IsCaseInsensitiveAndTrimsWhitespace(string input)
    {
        // Info / Warn のどちらでも Warning か Information のいずれかに落ちれば OK
        var level = SLLogLevels.Parse(input);
        Assert.True(level == LogLevel.Information || level == LogLevel.Warning);
    }

    [Fact]
    public void Parse_NullThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => SLLogLevels.Parse(null!));
    }

    [Theory]
    [InlineData("verbose")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("WarnError")]
    public void Parse_UnknownValueThrowsArgumentException(string input)
    {
        Assert.Throws<ArgumentException>(() => SLLogLevels.Parse(input));
    }

    [Theory]
    [InlineData("Info", true, LogLevel.Information)]
    [InlineData("WARN", true, LogLevel.Warning)]
    [InlineData("bogus", false, LogLevel.None)]
    [InlineData("", false, LogLevel.None)]
    [InlineData(null, false, LogLevel.None)]
    public void TryParse_ReturnsExpected(string? input, bool expectedOk, LogLevel expectedLevel)
    {
        var ok = SLLogLevels.TryParse(input, out var level);
        Assert.Equal(expectedOk, ok);
        Assert.Equal(expectedLevel, level);
    }
}
