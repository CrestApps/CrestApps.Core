namespace CrestApps.Core.AI.Resilience.Models;

/// <summary>
/// Represents the default retry settings used by <c>UseDefaultResilience()</c> for Microsoft.Extensions.AI client pipelines.
/// </summary>
public sealed class AIChatClientRetryOptions
{
    /// <summary>
    /// Gets or sets the maximum number of retries to perform after a rate-limit failure.
    /// </summary>
    public int MaxRateLimitRetries { get; set; } = 5;

    /// <summary>
    /// Gets or sets the base delay applied between rate-limit retries.
    /// </summary>
    public TimeSpan RateLimitRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the retry backoff strategy.
    /// </summary>
    public Polly.DelayBackoffType BackoffType { get; set; } = Polly.DelayBackoffType.Exponential;

    /// <summary>
    /// Gets or sets a value indicating whether jitter is enabled for the default retry schedule.
    /// </summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum retry delay.
    /// </summary>
    public TimeSpan? MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(32);
}
