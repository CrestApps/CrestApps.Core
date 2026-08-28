using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Framework.AI.Security;

public sealed class DefaultChatRateLimiterTests
{
    [Fact]
    public async Task EvaluateAsync_WhenRateLimitDisabled_ReturnsAllowed()
    {
        var limiter = CreateLimiter(maxMessages: 0);
        var context = CreateContext();

        var result = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.False(result.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_UnderLimit_ReturnsAllowed()
    {
        var limiter = CreateLimiter(maxMessages: 5);
        var context = CreateContext();

        for (var i = 0; i < 5; i++)
        {
            var result = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

            Assert.False(result.IsThrottled);
        }
    }

    [Fact]
    public async Task EvaluateAsync_ExceedsLimit_ReturnsThrottled()
    {
        var limiter = CreateLimiter(maxMessages: 3);
        var context = CreateContext();

        // Send 3 messages (fills the window).
        for (var i = 0; i < 3; i++)
        {
            await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        }

        // The 4th should be throttled.
        var result = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.IsThrottled);
        Assert.True(result.RetryAfterSeconds > 0);
        Assert.Equal(3, result.CurrentCount);
        Assert.Equal(3, result.MaxAllowed);
    }

    [Fact]
    public async Task EvaluateAsync_AfterWindowExpires_AllowsAgain()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var limiter = CreateLimiter(maxMessages: 2, windowSeconds: 60, timeProvider: fakeTime);
        var context = CreateContext();

        // Fill the window.
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Blocked now.
        var blocked = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        Assert.True(blocked.IsThrottled);

        // Advance time past the window.
        fakeTime.Advance(TimeSpan.FromSeconds(61));

        // Should be allowed again.
        var allowed = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        Assert.False(allowed.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_DifferentSessions_TrackedIndependently()
    {
        var limiter = CreateLimiter(
            maxMessages: 2,
            rateLimitingOptions: new AIChatRateLimitingOptions
            {
                AnonymousMessagePartitions = ChatRateLimitPartition.Session,
            });
        var context1 = CreateContext(sessionId: "session-1");
        var context2 = CreateContext(sessionId: "session-2");

        // Fill session-1.
        await limiter.EvaluateAsync(context1, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context1, TestContext.Current.CancellationToken);

        // Session-1 is throttled.
        var result1 = await limiter.EvaluateAsync(context1, TestContext.Current.CancellationToken);
        Assert.True(result1.IsThrottled);

        // Session-2 is still allowed.
        var result2 = await limiter.EvaluateAsync(context2, TestContext.Current.CancellationToken);
        Assert.False(result2.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_KeysByUserIdentity_WhenAvailable()
    {
        var limiter = CreateLimiter(maxMessages: 2);
        var user = TestHelpers.CreateClaimsPrincipal("user-42");
        var context1 = CreateContext(sessionId: "session-A", user: user);
        var context2 = CreateContext(sessionId: "session-B", user: user);

        // Both contexts share the same user identity key.
        await limiter.EvaluateAsync(context1, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context2, TestContext.Current.CancellationToken);

        // Third request from the same user (different session) should be throttled.
        var result = await limiter.EvaluateAsync(context1, TestContext.Current.CancellationToken);
        Assert.True(result.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_KeysAnonymousTrafficByVisitorIdAcrossSessions()
    {
        var limiter = CreateLimiter(maxMessages: 2);
        var context1 = CreateContext(sessionId: "session-A", visitorId: "visitor-1");
        var context2 = CreateContext(sessionId: "session-B", visitorId: "visitor-1");

        await limiter.EvaluateAsync(context1, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context2, TestContext.Current.CancellationToken);

        var result = await limiter.EvaluateAsync(context1, TestContext.Current.CancellationToken);

        Assert.True(result.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_UsesRemoteAddressHashFallbackWhenVisitorIdChanges()
    {
        var limiter = CreateLimiter(maxMessages: 2);
        var context1 = CreateContext(sessionId: "session-A", visitorId: "visitor-1", remoteAddressHash: "ip-1");
        var context2 = CreateContext(sessionId: "session-B", visitorId: "visitor-2", remoteAddressHash: "ip-1");

        await limiter.EvaluateAsync(context1, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context2, TestContext.Current.CancellationToken);

        var result = await limiter.EvaluateAsync(context1, TestContext.Current.CancellationToken);

        Assert.True(result.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_UsesPlainTextRemoteAddressWhenConfigured()
    {
        var limiter = CreateLimiter(
            maxMessages: 2,
            rateLimitingOptions: new AIChatRateLimitingOptions
            {
                AnonymousMessagePartitions = ChatRateLimitPartition.NetworkAddress,
            });
        var context1 = CreateContext(sessionId: "session-A", visitorId: "visitor-1", remoteAddress: "203.0.113.10");
        var context2 = CreateContext(sessionId: "session-B", visitorId: "visitor-2", remoteAddress: "203.0.113.10");

        await limiter.EvaluateAsync(context1, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context2, TestContext.Current.CancellationToken);

        var result = await limiter.EvaluateAsync(context1, TestContext.Current.CancellationToken);

        Assert.True(result.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_ProfileOverridesRateLimit()
    {
        var limiter = CreateLimiter(maxMessages: 100);
        var context = CreateContext();
        context.Profile = new AIProfile { ItemId = "profile-1" };
        context.Profile.WithSettings(new PromptSecurityProfileSettings
        {
            MaxMessagesPerWindow = 2,
            RateLimitWindow = TimeSpan.FromMinutes(1),
        });

        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        var result = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_ProfileDisablesRateLimit()
    {
        var limiter = CreateLimiter(maxMessages: 2);
        var context = CreateContext();
        context.Profile = new AIProfile { ItemId = "profile-1" };
        context.Profile.WithSettings(new PromptSecurityProfileSettings
        {
            MaxMessagesPerWindow = 0,
        });

        // Even though site-level is 2, the profile overrides to 0 (disabled).
        for (var i = 0; i < 10; i++)
        {
            var result = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

            Assert.False(result.IsThrottled);
        }
    }

    [Fact]
    public async Task EvaluateAsync_ProfileOverridesCount_InheritsWindowFromSite()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);

        // Site default: 100 messages within a 60 second window.
        var limiter = CreateLimiter(maxMessages: 100, windowSeconds: 60, timeProvider: fakeTime);
        var context = CreateContext();
        context.Profile = new AIProfile { ItemId = "profile-1" };

        // Profile overrides only the count and leaves the window null so it inherits the site window.
        context.Profile.WithSettings(new PromptSecurityProfileSettings
        {
            MaxMessagesPerWindow = 2,
        });

        // The count override applies (throttled after 2, not the site default of 100).
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        var blocked = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        Assert.True(blocked.IsThrottled);

        // Advancing past the inherited 60 second site window frees the quota again,
        // proving the null profile window fell back to the site setting.
        fakeTime.Advance(TimeSpan.FromSeconds(61));

        var allowed = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.False(allowed.IsThrottled);
    }

    [Fact]
    public async Task Reset_ClearsSessionTracking()
    {
        var limiter = CreateLimiter(
            maxMessages: 2,
            rateLimitingOptions: new AIChatRateLimitingOptions
            {
                AnonymousMessagePartitions = ChatRateLimitPartition.Session,
            });
        var context = CreateContext(sessionId: "session-to-reset");

        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Throttled.
        var blocked = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        Assert.True(blocked.IsThrottled);

        // Reset.
        limiter.Reset("session:session-to-reset");

        // Should be allowed again.
        var allowed = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        Assert.False(allowed.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_SlidingWindow_EvictsOldEntries()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var limiter = CreateLimiter(maxMessages: 3, windowSeconds: 60, timeProvider: fakeTime);
        var context = CreateContext();

        // Send 3 messages at T=0.
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Blocked at T=0.
        var blocked = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        Assert.True(blocked.IsThrottled);

        // Advance 30 seconds (first 3 still in window).
        fakeTime.Advance(TimeSpan.FromSeconds(30));
        blocked = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        Assert.True(blocked.IsThrottled);

        // Advance to 61 seconds total (first 3 expire from window).
        fakeTime.Advance(TimeSpan.FromSeconds(31));
        var allowed = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        Assert.False(allowed.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_AnonymousBurstTier_ThrottlesWithinShortWindow()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var limiter = CreateLimiter(
            maxMessages: 1000,
            timeProvider: fakeTime,
            anonymousMessageTiers:
            [
                new ChatRateLimitTier(2, TimeSpan.FromSeconds(30)),
                new ChatRateLimitTier(5, TimeSpan.FromMinutes(5)),
            ]);
        var context = CreateContext();

        // The 30-second burst tier allows only 2.
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
            maxMessages: 1000,
            timeProvider: fakeTime,
            anonymousMessageTiers:
            [
                new ChatRateLimitTier(2, TimeSpan.FromSeconds(30)),
                new ChatRateLimitTier(3, TimeSpan.FromMinutes(5)),
            ]);
        var context = CreateContext();

        // Two messages at T=0 fill the 30-second burst tier.
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        // Past the burst window: the burst tier resets, so a third message is allowed.
        fakeTime.Advance(TimeSpan.FromSeconds(31));
        var allowed = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        Assert.False(allowed.IsThrottled);

        // But the 5-minute tier now holds 3, so the next message is throttled by that tier.
        var result = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.IsThrottled);
        Assert.Equal(3, result.CurrentCount);
        Assert.Equal(3, result.MaxAllowed);
    }

    [Fact]
    public async Task EvaluateAsync_AuthenticatedUser_BypassesAnonymousTiers()
    {
        var limiter = CreateLimiter(
            maxMessages: 100,
            anonymousMessageTiers: [new ChatRateLimitTier(1, TimeSpan.FromMinutes(1))]);
        var context = CreateContext(user: TestHelpers.CreateClaimsPrincipal("user-99"));

        // The anonymous tier caps at 1, but an authenticated caller uses the single-window limit.
        for (var i = 0; i < 5; i++)
        {
            var result = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

            Assert.False(result.IsThrottled);
        }
    }

    [Fact]
    public async Task EvaluateAsync_AuthenticatedUsers_ShareTheirNetworkAddressBucket()
    {
        // Two different authenticated users behind the same IP share the network-address bucket, so
        // the second is throttled once the first has consumed the per-IP allowance.
        var limiter = CreateLimiter(maxMessages: 2);
        var userA = CreateContext(sessionId: "a", user: TestHelpers.CreateClaimsPrincipal("user-A"), remoteAddressHash: "ip-1");
        var userB = CreateContext(sessionId: "b", user: TestHelpers.CreateClaimsPrincipal("user-B"), remoteAddressHash: "ip-1");

        await limiter.EvaluateAsync(userA, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(userA, TestContext.Current.CancellationToken);

        // user-B has sent nothing, but the shared ip-1 bucket is already full.
        var result = await limiter.EvaluateAsync(userB, TestContext.Current.CancellationToken);

        Assert.True(result.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_AuthenticatedUsageCountsTowardIpBucket_SoLoggingOutDoesNotReset()
    {
        // Authenticated messages accrue against the shared network-address bucket, so a caller cannot
        // reset their per-IP anonymous allowance by logging out.
        var limiter = CreateLimiter(
            maxMessages: 100, // Generous per-user limit so the user cap is not what trips here.
            anonymousMessageTiers: [new ChatRateLimitTier(2, TimeSpan.FromMinutes(5))]);
        var authenticated = CreateContext(sessionId: "s", user: TestHelpers.CreateClaimsPrincipal("user-1"), remoteAddressHash: "ip-1");
        var anonymousSameIp = CreateContext(sessionId: "s2", user: null, remoteAddressHash: "ip-1");

        // Two messages while authenticated (well under the per-user limit).
        await limiter.EvaluateAsync(authenticated, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(authenticated, TestContext.Current.CancellationToken);

        // Logging out (same IP) does not grant a fresh allowance: the anonymous tier already sees the
        // two authenticated messages on the shared ip-1 bucket.
        var result = await limiter.EvaluateAsync(anonymousSameIp, TestContext.Current.CancellationToken);

        Assert.True(result.IsThrottled);
    }

    [Fact]
    public async Task EvaluateAsync_AuthenticatedUser_WithoutNetworkAddress_KeysByUserOnly()
    {
        // With no resolvable network address, authenticated throttling falls back to the per-user key.
        var limiter = CreateLimiter(maxMessages: 2);
        var context = CreateContext(user: TestHelpers.CreateClaimsPrincipal("user-1"));

        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);
        await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        var result = await limiter.EvaluateAsync(context, TestContext.Current.CancellationToken);

        Assert.True(result.IsThrottled);
    }

    private static DefaultChatRateLimiter CreateLimiter(
        int maxMessages,
        int windowSeconds = 60,
        AIChatRateLimitingOptions rateLimitingOptions = null,
        TimeProvider timeProvider = null,
        List<ChatRateLimitTier> anonymousMessageTiers = null)
    {
        var options = Options.Create(new PromptSecurityOptions
        {
            // Default to no tiers so the single-window fallback (MaxMessagesPerWindow) is exercised;
            // tier-specific tests pass an explicit list.
            AnonymousMessageRateLimitTiers = anonymousMessageTiers ?? [],
            MaxMessagesPerWindow = maxMessages,
            RateLimitWindow = TimeSpan.FromSeconds(windowSeconds),
        });

        return new DefaultChatRateLimiter(
            timeProvider ?? TimeProvider.System,
            Options.Create(rateLimitingOptions ?? new AIChatRateLimitingOptions()),
            options,
            NullLogger<DefaultChatRateLimiter>.Instance);
    }

    private static PromptSecurityContext CreateContext(
        string sessionId = "test-session",
        System.Security.Claims.ClaimsPrincipal user = null,
        string visitorId = null,
        string remoteAddressHash = null,
        string remoteAddress = null)
    {
        return new PromptSecurityContext
        {
            Prompt = "Hello",
            SessionId = sessionId,
            ProfileId = "profile-1",
            User = user,
            ConnectionId = "conn-1",
            VisitorId = visitorId,
            RemoteAddressHash = remoteAddressHash,
            RemoteAddress = remoteAddress,
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

internal static class TestHelpers
{
    public static System.Security.Claims.ClaimsPrincipal CreateClaimsPrincipal(string userId)
    {
        var claims = new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId),
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "Test");

        return new System.Security.Claims.ClaimsPrincipal(identity);
    }
}
