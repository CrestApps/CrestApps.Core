namespace CrestApps.Core.AI.Models;

/// <summary>
/// One row of an AI usage report: the metered requests that share a provider, model, kind of request, and,
/// depending on the grouping, a category or purpose, with every billable unit summed.
/// </summary>
public sealed class AIUsageReportRow
{
    /// <summary>
    /// Gets or sets the client (provider) name.
    /// </summary>
    public string ClientName { get; set; }

    /// <summary>
    /// Gets or sets the model name.
    /// </summary>
    public string ModelName { get; set; }

    /// <summary>
    /// Gets or sets the kind of request, one of <see cref="Completions.AIUsageOperationTypes"/>.
    /// </summary>
    public string OperationType { get; set; }

    /// <summary>
    /// Gets or sets the category, or <see langword="null"/> when the grouping does not include it.
    /// </summary>
    public string ContextType { get; set; }

    /// <summary>
    /// Gets or sets the purpose, or <see langword="null"/> when the grouping does not include it.
    /// </summary>
    public string Purpose { get; set; }

    /// <summary>
    /// Gets or sets the number of requests sent to the provider. Each model round trip, such as each step of a
    /// tool-calling loop, counts as one request.
    /// </summary>
    public int RequestCount { get; set; }

    /// <summary>
    /// Gets or sets the input tokens.
    /// </summary>
    public long InputTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the input tokens served from the provider's prompt cache.
    /// </summary>
    public long CachedInputTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the output tokens.
    /// </summary>
    public long OutputTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the output tokens spent on reasoning.
    /// </summary>
    public long ReasoningTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the input audio tokens.
    /// </summary>
    public long InputAudioTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the output audio tokens.
    /// </summary>
    public long OutputAudioTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the input tokens written to the provider's prompt cache.
    /// </summary>
    public long CacheWriteInputTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the cached input tokens that were audio.
    /// </summary>
    public long CachedInputAudioTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the input image tokens.
    /// </summary>
    public long InputImageTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the output image tokens.
    /// </summary>
    public long OutputImageTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the output tokens from accepted predicted outputs.
    /// </summary>
    public long AcceptedPredictionTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the rejected predicted output tokens, which are still billed as output tokens.
    /// </summary>
    public long RejectedPredictionTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the total tokens.
    /// </summary>
    public long TotalTokenCount { get; set; }

    /// <summary>
    /// Gets or sets the web searches the provider ran on its side.
    /// </summary>
    public long WebSearchCount { get; set; }

    /// <summary>
    /// Gets or sets the web pages the provider fetched on its side.
    /// </summary>
    public long WebFetchCount { get; set; }

    /// <summary>
    /// Gets or sets every other count the providers reported, summed by name.
    /// </summary>
    public Dictionary<string, long> AdditionalCounts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the milliseconds of audio transcribed.
    /// </summary>
    public long AudioDurationMs { get; set; }

    /// <summary>
    /// Gets or sets the characters synthesized.
    /// </summary>
    public long CharacterCount { get; set; }

    /// <summary>
    /// Gets or sets the images generated.
    /// </summary>
    public long ImageCount { get; set; }

    /// <summary>
    /// Gets or sets the average response latency in milliseconds across the requests that measured one.
    /// </summary>
    public double AverageResponseLatencyMs { get; set; }

    /// <summary>
    /// Gets or sets the currency of <see cref="EstimatedCost"/>, or <see langword="null"/> when the requests'
    /// deployments have no price.
    /// </summary>
    public string Currency { get; set; }

    /// <summary>
    /// Gets or sets the estimated cost from the prices set on the requests' deployments, or <see langword="null"/>
    /// when they have no price.
    /// </summary>
    public decimal? EstimatedCost { get; set; }
}
