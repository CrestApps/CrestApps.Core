using CrestApps.Core.AI.Security;
using CrestApps.Core.Startup.Shared.Services;

namespace CrestApps.Core.Tests.Framework.AI.Security;

public sealed class ChatRateLimitTierTextFormatterTests
{
    [Fact]
    public void Format_WritesOneLimitCommaWindowLinePerTier()
    {
        var text = ChatRateLimitTierTextFormatter.Format(
        [
            new ChatRateLimitTier(5, TimeSpan.FromSeconds(30)),
            new ChatRateLimitTier(500, TimeSpan.FromDays(1)),
        ]);

        var lines = text.Split(Environment.NewLine);

        Assert.Equal(2, lines.Length);
        Assert.Equal("5, 00:00:30", lines[0]);
        Assert.Equal("500, 1.00:00:00", lines[1]);
    }

    [Fact]
    public void Format_RoundTripsThroughTryParse()
    {
        List<ChatRateLimitTier> original =
        [
            new ChatRateLimitTier(5, TimeSpan.FromSeconds(30)),
            new ChatRateLimitTier(30, TimeSpan.FromMinutes(5)),
            new ChatRateLimitTier(150, TimeSpan.FromHours(1)),
            new ChatRateLimitTier(500, TimeSpan.FromDays(1)),
        ];

        var succeeded = ChatRateLimitTierTextFormatter.TryParse(
            ChatRateLimitTierTextFormatter.Format(original), out var parsed, out var error);

        Assert.True(succeeded);
        Assert.Null(error);
        Assert.Equal(original.Count, parsed.Count);

        for (var i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Limit, parsed[i].Limit);
            Assert.Equal(original[i].Window, parsed[i].Window);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_BlankInput_ReturnsEmptyAndSucceeds(string input)
    {
        var result = ChatRateLimitTierTextFormatter.TryParse(input, out var tiers, out var error);

        Assert.True(result);
        Assert.Null(error);
        Assert.Empty(tiers);
    }

    [Fact]
    public void TryParse_IgnoresBlankLinesAndTrimsWhitespace()
    {
        var result = ChatRateLimitTierTextFormatter.TryParse(
            "\n  5 , 00:00:30  \n\n 30, 00:05:00 \n",
            out var tiers,
            out var error);

        Assert.True(result);
        Assert.Null(error);
        Assert.Equal(2, tiers.Count);
        Assert.Equal(5, tiers[0].Limit);
        Assert.Equal(TimeSpan.FromSeconds(30), tiers[0].Window);
        Assert.Equal(30, tiers[1].Limit);
        Assert.Equal(TimeSpan.FromMinutes(5), tiers[1].Window);
    }

    [Theory]
    [InlineData("5", "format")]
    [InlineData("0, 00:00:30", "positive whole number")]
    [InlineData("-1, 00:00:30", "positive whole number")]
    [InlineData("abc, 00:00:30", "positive whole number")]
    [InlineData("5, notatime", "valid window")]
    [InlineData("5, 00:00:00", "valid window")]
    public void TryParse_InvalidLine_FailsWithErrorAndEmptyTiers(string input, string expectedErrorFragment)
    {
        var result = ChatRateLimitTierTextFormatter.TryParse(input, out var tiers, out var error);

        Assert.False(result);
        Assert.Empty(tiers);
        Assert.NotNull(error);
        Assert.Contains(expectedErrorFragment, error);
    }

    [Fact]
    public void TryParse_ReportsOffendingLineNumber()
    {
        var result = ChatRateLimitTierTextFormatter.TryParse(
            "5, 00:00:30\n30, 00:05:00\nbroken",
            out _,
            out var error);

        Assert.False(result);
        Assert.Contains("Line 3", error);
    }
}
