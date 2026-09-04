#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only.
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// The resolved inputs used by <see cref="IRealtimeSessionConfigurator"/> to build session options.
/// </summary>
public sealed class RealtimeSessionConfiguratorContext
{
    /// <summary>
    /// Gets the provider model / deployment name for the realtime session.
    /// </summary>
    public string Model { get; init; }

    /// <summary>
    /// Gets the full instructions (system message) produced by the orchestration PREPARE pipeline,
    /// including any RAG guidance. Mapped verbatim to <see cref="RealtimeSessionOptions.Instructions"/>.
    /// </summary>
    public string Instructions { get; init; }

    /// <summary>
    /// Gets the resolved voice id for the session output audio.
    /// </summary>
    public string Voice { get; init; }

    /// <summary>
    /// Gets the tools advertised to (and invoked by) the session.
    /// </summary>
    public IReadOnlyList<AITool> Tools { get; init; } = [];

    /// <summary>
    /// Gets an optional cap on output tokens per response.
    /// </summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>
    /// Gets a value indicating whether user barge-in interrupts the model's audio. Defaults to <see langword="true"/>.
    /// </summary>
    public bool AllowInterruption { get; init; } = true;

    /// <summary>
    /// Gets the PCM sample rate (Hz) of the audio the client sends. Defaults to 24000.
    /// </summary>
    public int InputSampleRate { get; init; } = 24000;

    /// <summary>
    /// Gets the PCM sample rate (Hz) of the audio the model returns. Defaults to 24000.
    /// </summary>
    public int OutputSampleRate { get; init; } = 24000;

    /// <summary>
    /// Gets the transcription model used to transcribe the user's input audio so their words appear in
    /// the transcript. Defaults to <c>whisper-1</c>. Set to <see langword="null"/> to disable input transcription.
    /// </summary>
    public string InputTranscriptionModel { get; init; } = "whisper-1";

    /// <summary>
    /// Gets an optional BCP-47 language hint for input transcription (e.g. "en").
    /// </summary>
    public string SpeechLanguage { get; init; }

    /// <summary>
    /// Gets an optional server voice-activity silence duration (milliseconds) — how long a pause the model
    /// waits after speech stops before treating the turn as complete. <see langword="null"/> uses the provider default.
    /// </summary>
    public int? SilenceDurationMs { get; init; }

    /// <summary>
    /// Gets an optional server voice-activity detection threshold (0.0–1.0). Higher values require louder speech
    /// to register, rejecting background noise and residual echo. <see langword="null"/> uses the provider default.
    /// </summary>
    public float? VadThreshold { get; init; }

    /// <summary>
    /// Gets the turn-detection algorithm to request (see <see cref="RealtimeTurnDetectionTypes"/>), or
    /// <see langword="null"/> to use the configured default.
    /// </summary>
    public string TurnDetectionType { get; init; }

    /// <summary>
    /// Gets the semantic turn-detection eagerness (<c>low</c>, <c>medium</c>, <c>high</c>, <c>auto</c>), or
    /// <see langword="null"/> to use the configured default.
    /// </summary>
    public string TurnDetectionEagerness { get; init; }
}
