namespace CrestApps.Core.AI.OpenAI.Azure.Models;

/// <summary>
/// Represents the azure Client Options.
/// </summary>
public sealed class AzureClientOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the SDK retry policy is enabled for Azure OpenAI clients.
    /// </summary>
    public bool EnableDefaultRetryPolicy { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether enables Logging.
    /// </summary>
    public bool EnableLogging { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether enables Message Content Logging.
    /// </summary>
    public bool EnableMessageContentLogging { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether enables Message Logging.
    /// </summary>
    public bool EnableMessageLogging { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of retry attempts the Azure OpenAI SDK should perform.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets the base delay applied between Azure OpenAI SDK retry attempts.
    /// </summary>
    public TimeSpan RateLimitRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the retry backoff strategy applied by the Azure OpenAI SDK retry policy.
    /// </summary>
    public AzureRetryBackoffType BackoffType { get; set; } = AzureRetryBackoffType.Exponential;

    /// <summary>
    /// Gets or sets a value indicating whether jitter is enabled for Azure OpenAI SDK retries.
    /// </summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum retry delay applied by the Azure OpenAI SDK retry policy.
    /// </summary>
    public TimeSpan? MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(32);
}
