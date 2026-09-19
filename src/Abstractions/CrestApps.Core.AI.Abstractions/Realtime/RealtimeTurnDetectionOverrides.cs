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
    /// Gets a value indicating whether the provider generates a reply as soon as it ends the user's turn.
    /// Defaults to <see langword="true"/>, which is what makes a session feel live.
    /// <para>
    /// Set to <see langword="false"/> when the host has work to do between hearing the user and answering them —
    /// notably preemptive knowledge retrieval, which needs the transcript, and so cannot run before a reply the
    /// provider has already started generating.
    /// </para>
    /// </summary>
    public bool CreateResponse { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether any override carries a value.
    /// </summary>
    public bool HasValues => !string.IsNullOrWhiteSpace(Type) || !string.IsNullOrWhiteSpace(Eagerness) || SilenceDurationMs.HasValue || Threshold.HasValue || !CreateResponse;
}
