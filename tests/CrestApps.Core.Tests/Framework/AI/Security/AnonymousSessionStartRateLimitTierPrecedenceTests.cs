using CrestApps.Core.AI.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Framework.AI.Security;

/// <summary>
/// Reproduces the production report: an operator sets "Maximum anonymous sessions per window" to 5,
/// yet the limiter logs <c>Count=10/10</c> for an anonymous visitor. The cause is not an
/// over-counting counter — it is that <see cref="PromptSecurityOptions.AnonymousSessionStartRateLimitTiers"/>
/// take precedence over the single-window <see cref="PromptSecurityOptions.MaxAnonymousSessionsPerWindow"/>
/// fallback (see <see cref="DefaultChatSessionStartRateLimiter"/>). Because the shipped defaults populate
/// the tiers with a <c>10 / 5-minute</c> tier, the operator's "5" is silently ignored.
/// </summary>
public sealed class AnonymousSessionStartRateLimitTierPrecedenceTests
{
    /// <summary>
    /// Faithful reproduction of the production log using the shipped default tiers and the operator's
    /// configured single-window cap of 5. An anonymous visitor is allowed to start 10 sessions inside
    /// the 5-minute tier — twice the configured 5 — and only the 11th start is throttled, reporting the
    /// tier limit (10/10) rather than the configured 5.
    /// </summary>
    [Fact]
    public async Task DefaultTiersOverrideConfiguredMax_AnonymousVisitorThrottlesAt10Not5()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);

        // Mirror production: the operator set "Maximum anonymous sessions per window" to 5, but the
        // tier textarea below it still holds the shipped defaults (including the 10 / 5-minute tier).
        var limiter = CreateLimiter(
            maxAnonymousSessionsPerWindow: 5,
            tiers: DefaultAnonymousSessionStartTiers(),
            timeProvider: fakeTime);

        var context = CreateAnonymousContext();

        // Space the starts 20 seconds apart. This keeps at most two starts inside the 30-second burst
        // tier (limit 5) so it never trips, while accumulating toward the 10 / 5-minute tier.
        for (var i = 0; i < 10; i++)
        {
            var allowed = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

            Assert.False(allowed.IsThrottled, $"Start #{i + 1} should be allowed under the 10 / 5-minute tier.");

            fakeTime.Advance(TimeSpan.FromSeconds(20));
        }

        // The 11th start within the 5-minute window is throttled by the tier, not by the operator's 5.
        var throttled = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.True(throttled.IsThrottled);
        Assert.Equal(10, throttled.CurrentCount);
        Assert.Equal(10, throttled.MaxAllowed);
    }

    /// <summary>
    /// The operator's intent — "no more than 5 anonymous sessions per window" — is honored only when
    /// the tiers list is cleared. This documents both the workaround and the fix direction: the
    /// single-window cap is a fallback that the non-empty default tiers suppress.
    /// </summary>
    [Fact]
    public async Task WithTiersCleared_ConfiguredMaxIsHonored_ThrottlesAt5()
    {
        var limiter = CreateLimiter(
            maxAnonymousSessionsPerWindow: 5,
            tiers: [],
            timeProvider: TimeProvider.System);

        var context = CreateAnonymousContext();

        for (var i = 0; i < 5; i++)
        {
            var allowed = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

            Assert.False(allowed.IsThrottled, $"Start #{i + 1} should be allowed under the configured cap of 5.");
        }

        var throttled = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.True(throttled.IsThrottled);
        Assert.Equal(5, throttled.CurrentCount);
        Assert.Equal(5, throttled.MaxAllowed);
    }

    /// <summary>
    /// Minimal, deterministic form of the same defect isolated to a single tier: with a lone
    /// <c>10 / 5-minute</c> tier present, a configured cap of 5 is ignored and the visitor reaches 10
    /// before being throttled.
    /// </summary>
    [Fact]
    public async Task SingleTier_OverridesConfiguredMax()
    {
        var limiter = CreateLimiter(
            maxAnonymousSessionsPerWindow: 5,
            tiers: [new ChatRateLimitTier { Limit = 10, Window = TimeSpan.FromMinutes(5) }],
            timeProvider: TimeProvider.System);

        var context = CreateAnonymousContext();

        for (var i = 0; i < 10; i++)
        {
            var allowed = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

            Assert.False(allowed.IsThrottled, $"Start #{i + 1} should be allowed; the configured cap of 5 is ignored.");
        }

        var throttled = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.True(throttled.IsThrottled);
        Assert.Equal(10, throttled.CurrentCount);
        Assert.Equal(10, throttled.MaxAllowed);
    }

    private static List<ChatRateLimitTier> DefaultAnonymousSessionStartTiers()
    {
        // Kept in sync with PromptSecurityOptions.AnonymousSessionStartRateLimitTiers defaults; pinned by
        // RateLimitDefaultsTests.
        return new PromptSecurityOptions().AnonymousSessionStartRateLimitTiers;
    }

    private static DefaultChatSessionStartRateLimiter CreateLimiter(
        int maxAnonymousSessionsPerWindow,
        List<ChatRateLimitTier> tiers,
        TimeProvider timeProvider)
    {
        return new DefaultChatSessionStartRateLimiter(
            timeProvider,
            Options.Create(new AIChatRateLimitingOptions()),
            Options.Create(new PromptSecurityOptions
            {
                MaxAnonymousSessionsPerWindow = maxAnonymousSessionsPerWindow,
                AnonymousSessionRateLimitWindow = TimeSpan.FromMinutes(10),
                AnonymousSessionStartRateLimitTiers = tiers,
            }),
            NullLogger<DefaultChatSessionStartRateLimiter>.Instance);
    }

    private static PromptSecurityContext CreateAnonymousContext()
    {
        return new PromptSecurityContext
        {
            User = null,
            VisitorId = "4zzzbzg2f1tngzdgnjgb8wsvdk",
            RemoteAddressHash = "ip-hash-1",
        };
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset startTime)
        {
            _utcNow = startTime;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
        }
    }
}
