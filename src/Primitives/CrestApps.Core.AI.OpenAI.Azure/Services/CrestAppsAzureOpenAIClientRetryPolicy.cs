using System.ClientModel.Primitives;
using CrestApps.Core.AI.OpenAI.Azure.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.OpenAI.Azure.Services;

internal sealed class CrestAppsAzureOpenAIClientRetryPolicy : ClientRetryPolicy
{
    private readonly AzureRetryBackoffType _backoffType;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan? _maxRetryDelay;
    private readonly bool _useJitter;

    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsAzureOpenAIClientRetryPolicy"/> class.
    /// </summary>
    /// <param name="maxRetries">The maximum number of retries to attempt.</param>
    /// <param name="baseDelay">The base retry delay.</param>
    /// <param name="backoffType">The retry backoff strategy.</param>
    /// <param name="useJitter">Whether jitter is enabled.</param>
    /// <param name="maxRetryDelay">The maximum retry delay.</param>
    /// <param name="enableLogging">Whether client-wide logging is enabled.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public CrestAppsAzureOpenAIClientRetryPolicy(
        int maxRetries,
        TimeSpan baseDelay,
        AzureRetryBackoffType backoffType,
        bool useJitter,
        TimeSpan? maxRetryDelay,
        bool enableLogging,
        ILoggerFactory loggerFactory)
        : base(maxRetries, enableLogging, loggerFactory)
    {
        _baseDelay = baseDelay < TimeSpan.Zero
            ? TimeSpan.Zero
            : baseDelay;
        _backoffType = backoffType;
        _useJitter = useJitter;
        _maxRetryDelay = maxRetryDelay is { } configuredMaxRetryDelay && configuredMaxRetryDelay < TimeSpan.Zero
            ? null
            : maxRetryDelay;
    }

    /// <summary>
    /// Gets the next delay before the Azure OpenAI SDK should retry the request.
    /// </summary>
    /// <param name="message">The pipeline message.</param>
    /// <param name="tryCount">The current try count.</param>
    /// <returns>The amount of time to wait before retrying.</returns>
    protected override TimeSpan GetNextDelay(PipelineMessage message, int tryCount)
    {
        var multiplier = _backoffType switch
        {
            AzureRetryBackoffType.Constant => 1D,
            _ => Math.Pow(2, Math.Max(tryCount - 1, 0)),
        };

        var delayMs = _baseDelay.TotalMilliseconds * multiplier;

        if (_useJitter && delayMs > 0)
        {
            delayMs *= 1D + Random.Shared.NextDouble();
        }

        if (_maxRetryDelay is { } maxRetryDelay)
        {
            delayMs = Math.Min(delayMs, maxRetryDelay.TotalMilliseconds);
        }

        if (delayMs <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromMilliseconds(delayMs);
    }
}
