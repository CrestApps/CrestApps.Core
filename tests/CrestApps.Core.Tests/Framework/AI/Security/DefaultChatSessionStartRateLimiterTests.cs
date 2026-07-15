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

    private static DefaultChatSessionStartRateLimiter CreateLimiter(
        int maxSessions,
        AIChatRateLimitingOptions rateLimitingOptions = null)
    {
        return new DefaultChatSessionStartRateLimiter(
            TimeProvider.System,
            Options.Create(rateLimitingOptions ?? new AIChatRateLimitingOptions()),
            Options.Create(new PromptSecurityOptions
            {
                MaxAnonymousSessionsPerWindow = maxSessions,
                AnonymousSessionRateLimitWindow = TimeSpan.FromMinutes(10),
            }),
            NullLogger<DefaultChatSessionStartRateLimiter>.Instance);
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
