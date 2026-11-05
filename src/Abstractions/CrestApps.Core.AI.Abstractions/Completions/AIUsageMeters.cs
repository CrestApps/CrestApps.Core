namespace CrestApps.Core.AI.Completions;

/// <summary>
/// The units a deployment can be priced in, used by <see cref="Models.AIDeploymentPrice.Meter"/>. Each one maps to a
/// count that usage metering records on <see cref="Models.AICompletionUsageRecord"/>.
/// </summary>
public static class AIUsageMeters
{
    /// <summary>
    /// Each request sent to the provider.
    /// </summary>
    public const string Requests = "Requests";

    /// <summary>
    /// Input tokens. When the deployment also prices <see cref="CachedInputTokens"/> or <see cref="InputAudioTokens"/>,
    /// those tokens are taken out of the input tokens and billed at their own price.
    /// </summary>
    public const string InputTokens = "InputTokens";

    /// <summary>
    /// Input tokens served from the provider's prompt cache.
    /// </summary>
    public const string CachedInputTokens = "CachedInputTokens";

    /// <summary>
    /// Output tokens. When the deployment also prices <see cref="OutputAudioTokens"/>, those tokens are taken out of
    /// the output tokens and billed at their own price. Reasoning tokens are output tokens.
    /// </summary>
    public const string OutputTokens = "OutputTokens";

    /// <summary>
    /// Input audio tokens.
    /// </summary>
    public const string InputAudioTokens = "InputAudioTokens";

    /// <summary>
    /// Output audio tokens.
    /// </summary>
    public const string OutputAudioTokens = "OutputAudioTokens";

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
    /// Every meter, in the order an editor lists them, with the quantities a price is usually quoted for.
    /// </summary>
    public static IReadOnlyList<AIUsageMeterDescriptor> All { get; } =
    [
        new(InputTokens, "Input tokens", [new(1_000_000, "1M tokens"), new(1_000, "1K tokens")]),
        new(CachedInputTokens, "Cached input tokens", [new(1_000_000, "1M tokens"), new(1_000, "1K tokens")]),
        new(OutputTokens, "Output tokens", [new(1_000_000, "1M tokens"), new(1_000, "1K tokens")]),
        new(InputAudioTokens, "Audio input tokens", [new(1_000_000, "1M tokens"), new(1_000, "1K tokens")]),
        new(OutputAudioTokens, "Audio output tokens", [new(1_000_000, "1M tokens"), new(1_000, "1K tokens")]),
        new(AudioSeconds, "Audio duration", [new(60, "minute"), new(3_600, "hour"), new(1, "second")]),
        new(Characters, "Characters", [new(1_000_000, "1M characters"), new(1_000, "1K characters")]),
        new(Images, "Images", [new(1, "image"), new(1_000, "1K images")]),
        new(Requests, "Requests", [new(1, "request"), new(1_000, "1K requests")]),
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
/// <param name="Quantities">The quantities a price is usually quoted for, the most common first.</param>
public sealed record AIUsageMeterDescriptor(string Name, string DisplayName, IReadOnlyList<AIUsageMeterQuantity> Quantities);

/// <summary>
/// A quantity a price can be quoted for, such as a million tokens or a minute of audio.
/// </summary>
/// <param name="Per">The number of the meter's base units.</param>
/// <param name="DisplayName">The name shown in editors, after "per".</param>
public sealed record AIUsageMeterQuantity(long Per, string DisplayName);
