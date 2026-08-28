using CrestApps.Core.AI.Chat.Hubs;
using CrestApps.Core.AI.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Framework.AI.Chat;

/// <summary>
/// Proves the session-start throttle message shown to a rate-limited visitor is helpful but does not
/// disclose the configured limit, the current count, or the exact retry delay — values that would let
/// an abuser tune around the throttle and that the operator asked to keep private.
/// </summary>
public sealed class AIChatHubSessionStartRateLimitMessageTests
{
    [Fact]
    public void GetSessionStartRateLimitMessage_IsGeneric_AndLeaksNoConfiguredValues()
    {
        var hub = new TestHub(new ServiceCollection().BuildServiceProvider());

        // A throttle result carrying the very numbers we must not surface (mirrors the production log:
        // Count=10/10, RetryAfter=94s).
        var result = RateLimitResult.Throttled(retryAfterSeconds: 94, currentCount: 10, maxAllowed: 10);

        var message = hub.GetSessionStartRateLimitMessageForTest(result);

        Assert.Equal(
            "You've reached the limit for starting new chats. Please wait a few minutes and try again.",
            message);

        // Regression guard: none of the throttle's numeric details may appear in the message.
        Assert.DoesNotContain("94", message);
        Assert.DoesNotContain("10", message);
        Assert.DoesNotContain("second", message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestHub : AIChatHubCore<IAIChatHubClient>
    {
        public TestHub(IServiceProvider services)
            : base(services, TimeProvider.System, NullLogger.Instance)
        {
        }

        public string GetSessionStartRateLimitMessageForTest(RateLimitResult result)
            => GetSessionStartRateLimitMessage(result);
    }
}
