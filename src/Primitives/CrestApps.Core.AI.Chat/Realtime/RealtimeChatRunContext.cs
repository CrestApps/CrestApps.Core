#nullable enable
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Inputs for a single <see cref="RealtimeChatSessionRunner"/> run. Works for both AI Chat sessions and
/// chat interactions: the <see cref="Resource"/> plus one of <see cref="ChatSession"/> /
/// <see cref="Interaction"/> establishes the ambient context for tools.
/// </summary>
public sealed class RealtimeChatRunContext
{
    /// <summary>
    /// Gets the resource driving the session (an <see cref="AIProfile"/> or <see cref="ChatInteraction"/>).
    /// </summary>
    public required object Resource { get; init; }

    /// <summary>
    /// Gets the session identifier used for persistence and client transcript addressing.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets the realtime deployment name, or <see langword="null"/> to use the site default.
    /// </summary>
    public string? RealtimeDeploymentName { get; init; }

    /// <summary>
    /// Gets the title stamped on persisted assistant turns (e.g. a profile's prompt subject).
    /// </summary>
    public string? PromptTitle { get; init; }

    /// <summary>
    /// Gets the chat session, when the resource is a profile-backed AI Chat session.
    /// </summary>
    public AIChatSession? ChatSession { get; init; }

    /// <summary>
    /// Gets the chat interaction, when the resource is a chat interaction.
    /// </summary>
    public ChatInteraction? Interaction { get; init; }

    /// <summary>
    /// Gets the voice id for the session output audio.
    /// </summary>
    public string? Voice { get; init; }

    /// <summary>
    /// Gets an optional BCP-47 language hint for input-audio transcription.
    /// </summary>
    public string? SpeechLanguage { get; init; }

    /// <summary>
    /// Gets an optional server voice-activity silence duration (milliseconds) before the model ends a turn.
    /// </summary>
    public int? SilenceDurationMs { get; init; }

    /// <summary>
    /// Gets an optional server voice-activity detection threshold (0.0–1.0).
    /// </summary>
    public float? VadThreshold { get; init; }

    /// <summary>
    /// Gets whether the user may interrupt (barge in on) the assistant while it is speaking. When false, the
    /// server voice-activity detector will not interrupt an in-progress response. Defaults to true.
    /// </summary>
    public bool AllowInterruption { get; init; } = true;

    /// <summary>
    /// Gets an optional hook invoked after each completed user utterance is persisted (e.g. title generation).
    /// </summary>
    public Func<string, CancellationToken, Task>? OnUserUtteranceAsync { get; init; }

    /// <summary>
    /// Gets an optional hook invoked after each completed assistant turn is persisted (e.g. save/commit).
    /// </summary>
    public Func<CancellationToken, Task>? OnAssistantCompletedAsync { get; init; }
}
