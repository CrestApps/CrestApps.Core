using System.Collections.Concurrent;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.Tests.Framework.AI.Security;

public sealed class MultiTierRateLimitEvaluatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ---- Normalize ----

    [Fact]
    public void Normalize_DropsNonPositiveLimitsAndWindows()
    {
        var normalized = MultiTierRateLimitEvaluator.Normalize(
        [
            new ChatRateLimitTier { Limit = 5, Window = TimeSpan.FromSeconds(30) },
            new ChatRateLimitTier { Limit = 0, Window = TimeSpan.FromSeconds(30) },   // limit <= 0
            new ChatRateLimitTier { Limit = 5, Window = TimeSpan.Zero },              // window <= 0
            new ChatRateLimitTier { Limit = -1, Window = TimeSpan.FromMinutes(1) },   // limit < 0
            null,
        ]);

        Assert.Single(normalized);
        Assert.Equal(5, normalized[0].Limit);
        Assert.Equal(TimeSpan.FromSeconds(30), normalized[0].Window);
    }

    [Fact]
    public void Normalize_NullInput_ReturnsEmpty()
    {
        Assert.Empty(MultiTierRateLimitEvaluator.Normalize(null));
    }

    // ---- MaxWindow ----

    [Fact]
    public void MaxWindow_ReturnsWidestWindow()
    {
        var max = MultiTierRateLimitEvaluator.MaxWindow(
        [
            new ChatRateLimitTier { Limit = 5, Window = TimeSpan.FromSeconds(30) },
            new ChatRateLimitTier { Limit = 500, Window = TimeSpan.FromDays(1) },
            new ChatRateLimitTier { Limit = 30, Window = TimeSpan.FromMinutes(5) },
        ]);

        Assert.Equal(TimeSpan.FromDays(1), max);
    }

    [Fact]
    public void MaxWindow_NullOrEmpty_ReturnsZero()
    {
        Assert.Equal(TimeSpan.Zero, MultiTierRateLimitEvaluator.MaxWindow(null));
        Assert.Equal(TimeSpan.Zero, MultiTierRateLimitEvaluator.MaxWindow([]));
    }

    // ---- Evaluate (single group convenience overload) ----

    [Fact]
    public void Evaluate_UnderLimit_AllowsAndRecords()
    {
        var windows = CreateWindows();
        var tiers = Tiers(new ChatRateLimitTier { Limit = 2, Window = TimeSpan.FromSeconds(60) });

        var result = MultiTierRateLimitEvaluator.Evaluate(windows, ["k"], tiers, T0);

        Assert.False(result.IsThrottled);
        Assert.Single(windows["k"].Timestamps);
    }

    [Fact]
    public void Evaluate_AtLimit_ThrottlesWithRetryAfterAndCounts()
    {
        var windows = CreateWindows();
        var tiers = Tiers(new ChatRateLimitTier { Limit = 2, Window = TimeSpan.FromSeconds(60) });

        MultiTierRateLimitEvaluator.Evaluate(windows, ["k"], tiers, T0);
        MultiTierRateLimitEvaluator.Evaluate(windows, ["k"], tiers, T0);

        var result = MultiTierRateLimitEvaluator.Evaluate(windows, ["k"], tiers, T0);

        Assert.True(result.IsThrottled);
        Assert.Equal(2, result.CurrentCount);
        Assert.Equal(2, result.MaxAllowed);
        Assert.Equal(60, result.RetryAfterSeconds);
        // A throttled request is not recorded.
        Assert.Equal(2, windows["k"].Timestamps.Count);
    }

    [Fact]
    public void Evaluate_MultiTier_ReportsMostConstrainingTier()
    {
        var windows = CreateWindows();
        // Both tiers are exceeded; the one that keeps the caller waiting longest is reported.
        var tiers = Tiers(
            new ChatRateLimitTier { Limit = 2, Window = TimeSpan.FromSeconds(60) },
            new ChatRateLimitTier { Limit = 2, Window = TimeSpan.FromHours(1) });

        MultiTierRateLimitEvaluator.Evaluate(windows, ["k"], tiers, T0);
        MultiTierRateLimitEvaluator.Evaluate(windows, ["k"], tiers, T0);

        var result = MultiTierRateLimitEvaluator.Evaluate(windows, ["k"], tiers, T0);

        Assert.True(result.IsThrottled);
        Assert.Equal(3600, result.RetryAfterSeconds);
        Assert.Equal(2, result.MaxAllowed);
    }

    [Fact]
    public void Evaluate_EvictsTimestampsBeyondMaxWindow()
    {
        var windows = CreateWindows();
        var tiers = Tiers(new ChatRateLimitTier { Limit = 2, Window = TimeSpan.FromSeconds(60) });

        MultiTierRateLimitEvaluator.Evaluate(windows, ["k"], tiers, T0);
        MultiTierRateLimitEvaluator.Evaluate(windows, ["k"], tiers, T0);

        // 61s later the original two timestamps fall outside the 60s window and are evicted.
        var result = MultiTierRateLimitEvaluator.Evaluate(windows, ["k"], tiers, T0.AddSeconds(61));

        Assert.False(result.IsThrottled);
        Assert.Single(windows["k"].Timestamps);
    }

    [Fact]
    public void Evaluate_MultipleKeys_ThrottlesIfAnyExceeds_AndDoesNotRecordWhenThrottled()
    {
        var windows = CreateWindows();
        var tiers = Tiers(new ChatRateLimitTier { Limit = 1, Window = TimeSpan.FromSeconds(60) });

        // First request fills both keys.
        MultiTierRateLimitEvaluator.Evaluate(windows, ["k1", "k2"], tiers, T0);

        // Second request throttles on k1 and must not record against k2.
        var result = MultiTierRateLimitEvaluator.Evaluate(windows, ["k1", "k2"], tiers, T0);

        Assert.True(result.IsThrottled);
        Assert.Single(windows["k1"].Timestamps);
        Assert.Single(windows["k2"].Timestamps);
    }

    // ---- Evaluate (key-group overload) ----

    [Fact]
    public void Evaluate_Groups_EnforcesEachGroupsOwnTiers()
    {
        var windows = CreateWindows();
        var groups = new[]
        {
            new RateLimitKeyGroup(["user"], Tiers(new ChatRateLimitTier { Limit = 5, Window = TimeSpan.FromSeconds(60) }), TimeSpan.FromSeconds(60)),
            new RateLimitKeyGroup(["ip"], Tiers(new ChatRateLimitTier { Limit = 1, Window = TimeSpan.FromSeconds(60) }), TimeSpan.FromSeconds(60)),
        };

        // First request is allowed and records both keys.
        Assert.False(MultiTierRateLimitEvaluator.Evaluate(windows, groups, T0).IsThrottled);

        // Second request is under the user tier (5) but exceeds the ip tier (1).
        var result = MultiTierRateLimitEvaluator.Evaluate(windows, groups, T0);

        Assert.True(result.IsThrottled);
        Assert.Equal(1, result.MaxAllowed);
    }

    [Fact]
    public void Evaluate_Groups_RecordsEachDistinctKeyOnce()
    {
        var windows = CreateWindows();
        var tiers = Tiers(new ChatRateLimitTier { Limit = 5, Window = TimeSpan.FromSeconds(60) });
        var groups = new[]
        {
            new RateLimitKeyGroup(["a", "shared"], tiers, TimeSpan.FromSeconds(60)),
            new RateLimitKeyGroup(["shared", "b"], tiers, TimeSpan.FromSeconds(60)),
        };

        MultiTierRateLimitEvaluator.Evaluate(windows, groups, T0);

        Assert.Single(windows["a"].Timestamps);
        Assert.Single(windows["b"].Timestamps);
        Assert.Single(windows["shared"].Timestamps);
    }

    [Fact]
    public void Evaluate_Groups_SkipsGroupsWithNoTiers()
    {
        var windows = CreateWindows();
        var groups = new[]
        {
            new RateLimitKeyGroup(["disabled"], [], TimeSpan.FromSeconds(60)),
        };

        var result = MultiTierRateLimitEvaluator.Evaluate(windows, groups, T0);

        Assert.False(result.IsThrottled);
        Assert.False(windows.ContainsKey("disabled"));
    }

    [Fact]
    public void Evaluate_Groups_PerGroupRetention_KeepsSharedHistoryForTheWiderGroup()
    {
        // Regression guard: a short-enforcement group that shares a key with a wider-retention group
        // must NOT evict the wider group's history. This is what stops an authenticated (short-window)
        // message from wiping the anonymous per-IP day history and enabling a logout reset.
        var windows = CreateWindows();
        var anonymousTiers = Tiers(new ChatRateLimitTier { Limit = 2, Window = TimeSpan.FromMinutes(5) });
        var authenticatedTiers = Tiers(new ChatRateLimitTier { Limit = 20, Window = TimeSpan.FromSeconds(60) });

        // Anonymous request records one timestamp on the shared ip bucket at T0.
        MultiTierRateLimitEvaluator.Evaluate(
            windows,
            new[] { new RateLimitKeyGroup(["ip"], anonymousTiers, TimeSpan.FromMinutes(5)) },
            T0);

        // Two minutes later an authenticated request touches the same ip bucket. Its enforcement
        // window is only 60s, but its retention matches the anonymous window (5m), so it must retain
        // the T0 timestamp.
        MultiTierRateLimitEvaluator.Evaluate(
            windows,
            new[] { new RateLimitKeyGroup(["ip"], authenticatedTiers, TimeSpan.FromMinutes(5)) },
            T0.AddMinutes(2));

        // The anonymous tier (2 / 5m) now sees both timestamps and throttles. If retention had been
        // the 60s enforcement window, the T0 timestamp would have been evicted and this would pass.
        var result = MultiTierRateLimitEvaluator.Evaluate(
            windows,
            new[] { new RateLimitKeyGroup(["ip"], anonymousTiers, TimeSpan.FromMinutes(5)) },
            T0.AddMinutes(2));

        Assert.True(result.IsThrottled);
        Assert.Equal(2, result.CurrentCount);
    }

    private static ConcurrentDictionary<string, SlidingWindowEntry> CreateWindows()
        => new(StringComparer.OrdinalIgnoreCase);

    private static List<ChatRateLimitTier> Tiers(params ChatRateLimitTier[] tiers)
        => MultiTierRateLimitEvaluator.Normalize(tiers);
}
