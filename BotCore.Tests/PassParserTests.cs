using BotMain;
using Xunit;

namespace BotCore.Tests;

public class PassParserTests
{
    [Fact]
    public void ParseValidResponse_ReturnsTrueAndFillsFields()
    {
        var ok = PassParser.TryParsePassInfoResponse(
            "PASS_INFO:87|1240|2000",
            out var level, out var xp, out var xpNeeded);

        Assert.True(ok);
        Assert.Equal(87, level);
        Assert.Equal(1240, xp);
        Assert.Equal(2000, xpNeeded);
    }

    [Fact]
    public void ParseNoPassInfo_ReturnsFalseAndKeepsZero()
    {
        var ok = PassParser.TryParsePassInfoResponse(
            "NO_PASS_INFO:not_received",
            out var level, out var xp, out var xpNeeded);

        Assert.False(ok);
        Assert.Equal(0, level);
        Assert.Equal(0, xp);
        Assert.Equal(0, xpNeeded);
    }

    [Fact]
    public void ParseEmptyOrNull_ReturnsFalse()
    {
        Assert.False(PassParser.TryParsePassInfoResponse(
            null, out _, out _, out _));
        Assert.False(PassParser.TryParsePassInfoResponse(
            "", out _, out _, out _));
        Assert.False(PassParser.TryParsePassInfoResponse(
            "   ", out _, out _, out _));
    }

    [Fact]
    public void ParseTooFewParts_ReturnsFalse()
    {
        var ok = PassParser.TryParsePassInfoResponse(
            "PASS_INFO:87|1240",
            out _, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void ParseNonIntegerField_ReturnsFalse()
    {
        var ok = PassParser.TryParsePassInfoResponse(
            "PASS_INFO:87|abc|2000",
            out _, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void ParseWrongPrefix_ReturnsFalse()
    {
        var ok = PassParser.TryParsePassInfoResponse(
            "RANK_INFO:87|1240|2000",
            out _, out _, out _);
        Assert.False(ok);
    }
}
