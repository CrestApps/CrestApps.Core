using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Framework.AI.Security;

public sealed class DefaultChatSessionStartRateLimiterTests
{
    [Fact]
    public async Task EvaluateAsync_WhenDisabled_ReturnsAllowed()
    {
        var limiter = CreateLimiter(maxSessions: 0);
        var result = await limiter.EvaluateAsync(CreateContext(), TestContext.Current.CancellationToken);

        Assert.False(result.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_AnonymousVisitorAcrossRequests_IsThrottled()
    {
        var limiter = CreateLimiter(maxSessions: 2);
        var context = CreateContext();

        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        var result = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_AuthenticatedUser_IsIgnored()
    {
        var limiter = CreateLimiter(maxSessions: 1);
        var context = CreateContext(user: TestHelpers.CreateClaimsPrincipal("user-1"));

        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        var result = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_UsesPlainTextRemoteAddressWhenConfigured()
    {
        var limiter = CreateLimiter(
            maxSessions: 2,
            rateLimitingOptions: new AIChatRateLimitingOptions
            {
                AnonymousSessionStartPartitions = ChatRateLimitPartition.NetworkAddress,
            });
        var context = CreateContext(remoteAddress: "198.51.100.25", remoteAddressHash: null);

        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        var result = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_ProfileOverrideLowersLimitBelowSiteDefault()
    {
        var limiter = CreateLimiter(maxSessions: 10);
        var context = CreateContext();
        context.Profile = new AIProfile
        {
            ItemId = "profile-1",
        };
        context.Profile.WithSettings(new PromptSecurityProfileSettings
        {
            MaxAnonymousSessionsPerWindow = 1,
            AnonymousSessionRateLimitWindow = TimeSpan.FromMinutes(10),
        });

        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        var result = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_ProfileOverrideRaisesLimitAboveSiteDefault()
    {
        var limiter = CreateLimiter(maxSessions: 1);
        var context = CreateContext();
        context.Profile = new AIProfile
        {
            ItemId = "profile-1",
        };
        context.Profile.WithSettings(new PromptSecurityProfileSettings
        {
            MaxAnonymousSessionsPerWindow = 3,
            AnonymousSessionRateLimitWindow = TimeSpan.FromMinutes(10),
        });

        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        var result = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_ProfileOverridesWindow_InheritsCountFromSite()
    {
        // Site default: 2 anonymous sessions per window.
        var limiter = CreateLimiter(maxSessions: 2);
        var context = CreateContext();
        context.Profile = new AIProfile
        {
            ItemId = "profile-1",
        };

        // Profile overrides only the window and leaves the count null so it inherits the site count.
        context.Profile.WithSettings(new PromptSecurityProfileSettings
        {
            AnonymousSessionRateLimitWindow = TimeSpan.FromMinutes(5),
        });

        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // The third start is throttled because the count fell back to the site default of 2.
        var result = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_AnonymousBurstTier_ThrottlesWithinShortWindow()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var limiter = CreateLimiter(
            maxSessions: 1000,
            timeProvider: fakeTime,
            anonymousSessionTiers:
            [
                new ChatRateLimitTier(2, TimeSpan.FromSeconds(30)),
                new ChatRateLimitTier(5, TimeSpan.FromMinutes(5)),
            ]);
        var context = CreateContext();

        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        var result = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.IsThrottled);
        Assert.Equal(2, result.CurrentCount);
        Assert.Equal(2, result.MaxAllowed);
    }

    [Fact]
    public async Task EvaluateAsync_AnonymousMultiTier_ThrottledByLongerTierAfterBurstWindowPasses()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var limiter = CreateLimiter(
            maxSessions: 1000,
            timeProvider: fakeTime,
            anonymousSessionTiers:
            [
                new ChatRateLimitTier(2, TimeSpan.FromSeconds(30)),
                new ChatRateLimitTier(3, TimeSpan.FromMinutes(5)),
            ]);
        var context = CreateContext();

        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Past the 30-second burst window a third start is allowed again.
        fakeTime.Advance(TimeSpan.FromSeconds(31));
        var allowed = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        Assert.False(allowed.IsThrottled);

        // The 5-minute tier now holds 3, throttling the next start.
        var result = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.IsThrottled);
        Assert.Equal(3, result.CurrentCount);
        Assert.Equal(3, result.MaxAllowed);
    }

    private static DefaultChatSessionStartRateLimiter CreateLimiter(
        int maxSessions,
        AIChatRateLimitingOptions rateLimitingOptions = null,
        TimeProvider timeProvider = null,
        List<ChatRateLimitTier> anonymousSessionTiers = null)
    {
        return new DefaultChatSessionStartRateLimiter(
            timeProvider ?? TimeProvider.System,
            Options.Create(rateLimitingOptions ?? new AIChatRateLimitingOptions()),
            Options.Create(new PromptSecurityOptions
            {
                // Default to no tiers so these cases exercise the single-window fallback governed by
                // MaxAnonymousSessionsPerWindow; tier-specific tests pass an explicit list.
                AnonymousSessionStartRateLimitTiers = anonymousSessionTiers ?? [],
                MaxAnonymousSessionsPerWindow = maxSessions,
                AnonymousSessionRateLimitWindow = TimeSpan.FromMinutes(10),
            }),
            NullLogger<DefaultChatSessionStartRateLimiter>.Instance);
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

    private static PromptSecurityContext CreateContext(
        System.Security.Claims.ClaimsPrincipal user = null,
        string remoteAddressHash = "ip-1",
        string remoteAddress = null)
    {
        return new PromptSecurityContext
        {
            User = user,
            VisitorId = "visitor-1",
            RemoteAddressHash = remoteAddressHash,
            RemoteAddress = remoteAddress,
        };
    }
}
