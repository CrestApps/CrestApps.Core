namespace CrestApps.Core.AI.Models;

/// <summary>
/// Classifies the purpose of an AI profile, determining how it is configured and used.
/// </summary>
public enum AIProfileType
{
    /// <summary>
    /// A profile that drives interactive chat sessions with a user.
    /// </summary>
    Chat,

    /// <summary>
    /// A profile used for background AI tasks that do not involve direct user interaction.
    /// </summary>
    Utility,

    /// <summary>
    /// A profile based on a predefined prompt template.
    /// </summary>
    TemplatePrompt,

    /// <summary>
    /// A profile that acts as an AI agent capable of orchestrating tools and sub-tasks.
    /// </summary>
    Agent,

    /// <summary>
    /// A profile that drives real-time, audio-only chat sessions with a user via a provider
    /// realtime session (speech-to-speech), rather than the speech-to-text and text-to-speech
    /// pipeline used by <see cref="Chat"/> conversation mode.
    /// </summary>
    RealtimeChat,
}
