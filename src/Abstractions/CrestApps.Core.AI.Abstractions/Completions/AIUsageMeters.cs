namespace CrestApps.Core.AI.Completions;

/// <summary>
/// The units a deployment can be priced in, used by <see cref="Models.AIDeploymentPrice.Meter"/>. Each one maps to a
/// count that usage metering records on <see cref="Models.AICompletionUsageRecord"/>.
/// </summary>
/// <remarks>
/// Providers report cached, cache-write, audio, and image tokens as part of the input and output token counts. When a
/// deployment prices one of them, those tokens are taken out of the input or output tokens and billed at their own
/// price; otherwise they stay in at the token price.
/// </remarks>
public static class AIUsageMeters
{
    /// <summary>
    /// Each request sent to the provider.
    /// </summary>
    public const string Requests = "Requests";

    /// <summary>
    /// Input tokens, less the cached, cache-write, audio, and image tokens the deployment prices on their own.
    /// </summary>
    public const string InputTokens = "InputTokens";

    /// <summary>
    /// Input tokens served from the provider's prompt cache (cache reads).
    /// </summary>
    public const string CachedInputTokens = "CachedInputTokens";

    /// <summary>
    /// Input tokens written to the provider's prompt cache, which some providers bill at a premium.
    /// </summary>
    public const string CacheWriteInputTokens = "CacheWriteInputTokens";

    /// <summary>
    /// Output tokens, less the audio and image tokens the deployment prices on their own. Reasoning tokens are output
    /// tokens.
    /// </summary>
    public const string OutputTokens = "OutputTokens";

    /// <summary>
    /// Input audio tokens.
    /// </summary>
    public const string InputAudioTokens = "InputAudioTokens";

    /// <summary>
    /// Cached input audio tokens, which realtime models bill at their own rate.
    /// </summary>
    public const string CachedInputAudioTokens = "CachedInputAudioTokens";

    /// <summary>
    /// Output audio tokens.
    /// </summary>
    public const string OutputAudioTokens = "OutputAudioTokens";

    /// <summary>
    /// Input image tokens.
    /// </summary>
    public const string InputImageTokens = "InputImageTokens";

    /// <summary>
    /// Output image tokens, for image models billed by token.
    /// </summary>
    public const string OutputImageTokens = "OutputImageTokens";

    /// <summary>
    /// Seconds of audio, for models billed by the length of the audio they transcribe or produce.
    /// </summary>
    public const string AudioSeconds = "AudioSeconds";

    /// <summary>
    /// Characters synthesized, for text-to-speech models billed by the character.
    /// </summary>
    public const string Characters = "Characters";

    /// <summary>
    /// Images generated.
    /// </summary>
    public const string Images = "Images";

    /// <summary>
    /// Web searches the provider ran on its side.
    /// </summary>
    public const string WebSearches = "WebSearches";

    /// <summary>
    /// Web pages the provider fetched on its side.
    /// </summary>
    public const string WebFetches = "WebFetches";

    /// <summary>
    /// Any other count the provider reported, named by <see cref="Models.AIDeploymentPrice.CountName"/> as it appears
    /// in <see cref="Models.AICompletionUsageRecord.AdditionalCounts"/>.
    /// </summary>
    public const string AdditionalCount = "AdditionalCount";

    private static readonly AIUsageMeterQuantity[] _tokenQuantities = [new(1_000_000, "1M tokens"), new(1_000, "1K tokens")];

    /// <summary>
    /// Every meter, in the order an editor lists them, with the quantities a price is usually quoted for.
    /// </summary>
    public static IReadOnlyList<AIUsageMeterDescriptor> All { get; } =
    [
        new(InputTokens, "Input tokens", "tokens", _tokenQuantities),
        new(CachedInputTokens, "Cached input tokens (cache reads)", "tokens", _tokenQuantities),
        new(CacheWriteInputTokens, "Cache write tokens", "tokens", _tokenQuantities),
        new(OutputTokens, "Output tokens", "tokens", _tokenQuantities),
        new(InputAudioTokens, "Audio input tokens", "tokens", _tokenQuantities),
        new(CachedInputAudioTokens, "Cached audio input tokens", "tokens", _tokenQuantities),
        new(OutputAudioTokens, "Audio output tokens", "tokens", _tokenQuantities),
        new(InputImageTokens, "Image input tokens", "tokens", _tokenQuantities),
        new(OutputImageTokens, "Image output tokens", "tokens", _tokenQuantities),
        new(AudioSeconds, "Audio duration", "seconds", [new(60, "minute"), new(3_600, "hour"), new(1, "second")]),
        new(Characters, "Characters", "characters", [new(1_000_000, "1M characters"), new(1_000, "1K characters")]),
        new(Images, "Images", "images", [new(1, "image"), new(1_000, "1K images")]),
        new(Requests, "Requests", "requests", [new(1, "request"), new(1_000, "1K requests")]),
        new(WebSearches, "Web searches", "searches", [new(1_000, "1K searches"), new(1, "search")]),
        new(WebFetches, "Web fetches", "fetches", [new(1_000, "1K fetches"), new(1, "fetch")]),
        new(AdditionalCount, "Other reported count", "units", [new(1, "unit"), new(1_000, "1K units"), new(1_000_000, "1M units")]),
    ];

    /// <summary>
    /// Finds a meter by name.
    /// </summary>
    /// <param name="name">The meter's name.</param>
    /// <returns>The meter, or <see langword="null"/> when no meter has that name.</returns>
    public static AIUsageMeterDescriptor Find(string name)
        => All.FirstOrDefault(meter => string.Equals(meter.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Describes a unit a deployment can be priced in.
/// </summary>
/// <param name="Name">The meter's name, one of <see cref="AIUsageMeters"/>.</param>
/// <param name="DisplayName">The name shown in editors.</param>
/// <param name="BaseUnit">The name of the base unit the meter counts, such as <c>tokens</c> or <c>seconds</c>.</param>
/// <param name="Quantities">The quantities a price is usually quoted for, the most common first.</param>
public sealed record AIUsageMeterDescriptor(string Name, string DisplayName, string BaseUnit, IReadOnlyList<AIUsageMeterQuantity> Quantities)
{
    /// <summary>
    /// Gets a value indicating whether a price on this meter names the count it prices
    /// (<see cref="Models.AIDeploymentPrice.CountName"/>).
    /// </summary>
    public bool RequiresCountName => Name == AIUsageMeters.AdditionalCount;
}

/// <summary>
/// A quantity a price can be quoted for, such as a million tokens or a minute of audio.
/// </summary>
/// <param name="Per">The number of the meter's base units.</param>
/// <param name="DisplayName">The name shown in editors, after "per".</param>
public sealed record AIUsageMeterQuantity(long Per, string DisplayName);
