using System.Net;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Resilience;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Telemetry;

public sealed class ResilienceTelemetryTests
{
    [Fact]
    public async Task UseDefaultResilience_WhenTheProviderRateLimits_CountsEachRetry()
    {
        using var collector = new TelemetryCollector();

        using var client = new RateLimitedChatClient(failures: 2)
            .AsBuilder()
            .UseDefaultResilience(options => options.RateLimitRetryDelay = TimeSpan.Zero)
            .Build();

        var response = await client.GetResponseAsync("Hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Done.", response.Text);

        // Other tests retry chat requests too, so this only asserts what these two retries must have recorded.
        var retries = collector.GetMeasurements("crestapps.core.ai.resilience.retries", CrestAppsTelemetry.OperationTag, AIUsageOperationTypes.Chat);

        Assert.Contains(retries, retry => Equals(retry.Tags[AITelemetry.AttemptTag], 1));
        Assert.Contains(retries, retry => Equals(retry.Tags[AITelemetry.AttemptTag], 2));
        Assert.All(retries, retry => Assert.Equal("rate_limit", retry.Tags[AITelemetry.ReasonTag]));
    }

    // Rate limits the first requests, then answers, like a provider that was briefly over quota.
    private sealed class RateLimitedChatClient : IChatClient
    {
        private int _remainingFailures;

        public RateLimitedChatClient(int failures)
        {
            _remainingFailures = failures;
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Decrement(ref _remainingFailures) >= 0)
            {
                throw new HttpRequestException("Too many requests.", null, HttpStatusCode.TooManyRequests);
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done.")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public object GetService(Type serviceType, object serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
        }
    }
}
