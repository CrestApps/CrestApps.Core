namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// Provider-neutral carrier for server turn-detection values that the Microsoft.Extensions.AI realtime options
/// cannot express. The configurator attaches an instance through <c>RealtimeSessionOptions.RawRepresentationFactory</c>,
/// and a provider that supports these knobs (for example Azure OpenAI) reads it when building the
/// <c>turn_detection</c> request.
/// </summary>
public sealed class RealtimeTurnDetectionOverrides
{
    /// <summary>
    /// Gets the turn-detection algorithm: <see cref="RealtimeTurnDetectionTypes.ServerVad"/> (a silence timer) or
    /// <see cref="RealtimeTurnDetectionTypes.SemanticVad"/> (the model judges whether the user has finished their
    /// thought). <see langword="null"/> uses the provider default.
    /// </summary>
    public string Type { get; init; }

    /// <summary>
    /// Gets how eagerly semantic turn detection ends the user's turn: <c>low</c>, <c>medium</c>, <c>high</c> or
    /// <c>auto</c>. Only meaningful for <see cref="RealtimeTurnDetectionTypes.SemanticVad"/>.
    /// </summary>
    public string Eagerness { get; init; }

    /// <summary>
    /// Gets the silence duration (milliseconds) the model waits after speech stops before ending the turn. Only
    /// meaningful for <see cref="RealtimeTurnDetectionTypes.ServerVad"/>.
    /// </summary>
    public int? SilenceDurationMs { get; init; }

    /// <summary>
    /// Gets the voice-activity detection threshold (0.0–1.0); higher requires louder speech to register. Only
    /// meaningful for <see cref="RealtimeTurnDetectionTypes.ServerVad"/>.
    /// </summary>
    public float? Threshold { get; init; }

    /// <summary>
    /// Gets a value indicating whether any override carries a value.
    /// </summary>
    public bool HasValues => !string.IsNullOrWhiteSpace(Type) || !string.IsNullOrWhiteSpace(Eagerness) || SilenceDurationMs.HasValue || Threshold.HasValue;
}

/// <summary>
/// The turn-detection algorithms a realtime provider may support.
/// </summary>
public static class RealtimeTurnDetectionTypes
{
    /// <summary>
    /// Ends the user's turn after a fixed stretch of silence. Fast, but a pause for thought mid-sentence ends the
    /// turn and the model answers half a question.
    /// </summary>
    public const string ServerVad = "server_vad";

    /// <summary>
    /// Ends the user's turn when the model judges the utterance complete, so a natural pause inside a sentence does
    /// not trigger a reply. This is what makes a spoken conversation feel like one.
    /// </summary>
    public const string SemanticVad = "semantic_vad";
}
