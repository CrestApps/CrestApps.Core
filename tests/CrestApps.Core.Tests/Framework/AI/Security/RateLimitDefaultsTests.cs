using CrestApps.Core.AI.Security;

namespace CrestApps.Core.Tests.Framework.AI.Security;

/// <summary>
/// Pins the shipped rate-limit defaults so a change to the tuned tiers or key partitions is a
/// deliberate, reviewed edit rather than an accidental regression.
/// </summary>
public sealed class RateLimitDefaultsTests
{
    [Fact]
    public void PromptSecurityOptions_AnonymousMessageTiers_HaveExpectedDefaults()
    {
        var options = new PromptSecurityOptions();

        AssertTiers(
            options.AnonymousMessageRateLimitTiers,
            (5, TimeSpan.FromSeconds(30)),
            (30, TimeSpan.FromMinutes(5)),
            (150, TimeSpan.FromHours(1)),
            (500, TimeSpan.FromDays(1)));
    }

    [Fact]
    public void PromptSecurityOptions_AnonymousSessionStartTiers_HaveExpectedDefaults()
    {
        var options = new PromptSecurityOptions();

        // Session starts use a stricter 5-minute cap (10) than messages (30).
        AssertTiers(
            options.AnonymousSessionStartRateLimitTiers,
            (5, TimeSpan.FromSeconds(30)),
            (10, TimeSpan.FromMinutes(5)),
            (150, TimeSpan.FromHours(1)),
            (500, TimeSpan.FromDays(1)));
    }

    [Fact]
    public void PromptSecurityOptions_SingleWindowFallbacks_HaveExpectedDefaults()
    {
        var options = new PromptSecurityOptions();

        Assert.Equal(20, options.MaxMessagesPerWindow);
        Assert.Equal(TimeSpan.FromMinutes(1), options.RateLimitWindow);
        Assert.Equal(20, options.MaxAnonymousSessionsPerWindow);
        Assert.Equal(TimeSpan.FromMinutes(10), options.AnonymousSessionRateLimitWindow);
    }

    [Fact]
    public void AIChatRateLimitingOptions_AuthenticatedMessagePartitions_IncludeUserAndNetworkAddress()
    {
        var options = new AIChatRateLimitingOptions();

        Assert.True(options.AuthenticatedMessagePartitions.HasFlag(ChatRateLimitPartition.AuthenticatedUser));
        Assert.True(options.AuthenticatedMessagePartitions.HasFlag(ChatRateLimitPartition.NetworkAddress));
    }

    [Fact]
    public void AIChatRateLimitingOptions_AnonymousMessagePartitions_IncludeAllVisitorSignals()
    {
        var options = new AIChatRateLimitingOptions();

        Assert.True(options.AnonymousMessagePartitions.HasFlag(ChatRateLimitPartition.Visitor));
        Assert.True(options.AnonymousMessagePartitions.HasFlag(ChatRateLimitPartition.NetworkAddress));
        Assert.True(options.AnonymousMessagePartitions.HasFlag(ChatRateLimitPartition.Session));
        Assert.True(options.AnonymousMessagePartitions.HasFlag(ChatRateLimitPartition.Connection));
    }

    [Fact]
    public void AIChatRateLimitingOptions_AnonymousSessionStartPartitions_AreVisitorAndNetworkAddress()
    {
        var options = new AIChatRateLimitingOptions();

        Assert.True(options.AnonymousSessionStartPartitions.HasFlag(ChatRateLimitPartition.Visitor));
        Assert.True(options.AnonymousSessionStartPartitions.HasFlag(ChatRateLimitPartition.NetworkAddress));
        Assert.False(options.AnonymousSessionStartPartitions.HasFlag(ChatRateLimitPartition.Session));
        Assert.False(options.AnonymousSessionStartPartitions.HasFlag(ChatRateLimitPartition.Connection));
    }

    private static void AssertTiers(
        List<ChatRateLimitTier> actual,
        params (int Limit, TimeSpan Window)[] expected)
    {
        Assert.Equal(expected.Length, actual.Count);

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Limit, actual[i].Limit);
            Assert.Equal(expected[i].Window, actual[i].Window);
        }
    }
}
