using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Security;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

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
        var limiter = CreateLimiter(maxMessages: 2);
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
    public async Task Reset_ClearsSessionTracking()
    {
        var limiter = CreateLimiter(maxMessages: 2);
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

    private static DefaultChatRateLimiter CreateLimiter(
        int maxMessages,
        int windowSeconds = 60,
        TimeProvider timeProvider = null)
    {
        var options = Options.Create(new PromptSecurityOptions
        {
            MaxMessagesPerWindow = maxMessages,
            RateLimitWindow = TimeSpan.FromSeconds(windowSeconds),
        });

        return new DefaultChatRateLimiter(
            timeProvider ?? TimeProvider.System,
            options,
            NullLogger<DefaultChatRateLimiter>.Instance);
    }

    private static PromptSecurityContext CreateContext(
        string sessionId = "test-session",
        System.Security.Claims.ClaimsPrincipal user = null)
    {
        return new PromptSecurityContext
        {
            Prompt = "Hello",
            SessionId = sessionId,
            ProfileId = "profile-1",
            User = user,
            ConnectionId = "conn-1",
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
