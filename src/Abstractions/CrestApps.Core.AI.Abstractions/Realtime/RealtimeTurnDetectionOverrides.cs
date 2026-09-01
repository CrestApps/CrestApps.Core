namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// Provider-neutral carrier for server voice-activity turn-detection values that the Microsoft.Extensions.AI
/// realtime options cannot express. The configurator attaches an instance through
/// <c>RealtimeSessionOptions.RawRepresentationFactory</c>, and a provider that supports these knobs (for
/// example Azure OpenAI) reads it when building the <c>turn_detection</c> request.
/// </summary>
public sealed class RealtimeTurnDetectionOverrides
{
    /// <summary>
    /// Gets the silence duration (milliseconds) the model waits after speech stops before ending the turn.
    /// </summary>
    public int? SilenceDurationMs { get; init; }

    /// <summary>
    /// Gets the voice-activity detection threshold (0.0–1.0); higher requires louder speech to register.
    /// </summary>
    public float? Threshold { get; init; }

    /// <summary>
    /// Gets a value indicating whether either override carries a value.
    /// </summary>
    public bool HasValues => SilenceDurationMs.HasValue || Threshold.HasValue;
}
