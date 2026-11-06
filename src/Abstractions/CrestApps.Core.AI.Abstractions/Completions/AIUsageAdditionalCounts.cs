namespace CrestApps.Core.AI.Completions;

/// <summary>
/// Keys a provider may set in <c>UsageDetails.AdditionalCounts</c> to report billable units that are not tokens.
/// Usage metering copies them onto the matching <see cref="Models.AICompletionUsageRecord"/> properties, so a
/// provider that knows a unit better than the metering client can measure it reports it this way.
/// </summary>
public static class AIUsageAdditionalCounts
{
    /// <summary>
    /// The length of the audio transcribed, in milliseconds. Copied to
    /// <see cref="Models.AICompletionUsageRecord.AudioDurationMs"/>.
    /// </summary>
    public const string AudioDurationMs = "AudioDurationMs";

    /// <summary>
    /// The number of characters synthesized. Copied to <see cref="Models.AICompletionUsageRecord.CharacterCount"/>.
    /// </summary>
    public const string CharacterCount = "CharacterCount";

    /// <summary>
    /// The number of images generated. Copied to <see cref="Models.AICompletionUsageRecord.ImageCount"/>.
    /// </summary>
    public const string ImageCount = "ImageCount";

    /// <summary>
    /// The number of input tokens written to the prompt cache. Copied to
    /// <see cref="Models.AICompletionUsageRecord.CacheWriteInputTokenCount"/>. Anthropic's
    /// <c>CacheCreationInputTokens</c> is read the same way.
    /// </summary>
    public const string CacheWriteInputTokenCount = "CacheWriteInputTokenCount";

    /// <summary>
    /// The number of cached input tokens that were audio. Copied to
    /// <see cref="Models.AICompletionUsageRecord.CachedInputAudioTokenCount"/>.
    /// </summary>
    public const string CachedInputAudioTokenCount = "CachedInputAudioTokenCount";

    /// <summary>
    /// The number of input tokens that were image tokens. Copied to
    /// <see cref="Models.AICompletionUsageRecord.InputImageTokenCount"/>. The OpenAI adapter's
    /// <c>InputTokenDetails.ImageTokenCount</c> is read the same way.
    /// </summary>
    public const string InputImageTokenCount = "InputImageTokenCount";

    /// <summary>
    /// The number of output tokens that were image tokens. Copied to
    /// <see cref="Models.AICompletionUsageRecord.OutputImageTokenCount"/>.
    /// </summary>
    public const string OutputImageTokenCount = "OutputImageTokenCount";

    /// <summary>
    /// The number of web searches the provider ran. Copied to <see cref="Models.AICompletionUsageRecord.WebSearchCount"/>.
    /// Anthropic's <c>WebSearchRequests</c> is read the same way.
    /// </summary>
    public const string WebSearchCount = "WebSearchCount";

    /// <summary>
    /// The number of web pages the provider fetched. Copied to <see cref="Models.AICompletionUsageRecord.WebFetchCount"/>.
    /// Anthropic's <c>WebFetchRequests</c> is read the same way.
    /// </summary>
    public const string WebFetchCount = "WebFetchCount";
}
