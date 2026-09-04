namespace CrestApps.Core.AI.Models;

/// <summary>
/// Defines the chat input/output mode for an AI chat profile or interaction.
/// Controls whether voice features (microphone, text-to-speech) are available.
/// </summary>
public enum ChatMode
{
    /// <summary>
    /// Standard text-only chat. No voice features are enabled.
    /// </summary>
    TextInput,

    /// <summary>
    /// Audio input mode. A microphone button is shown so users can
    /// dictate their prompts via speech-to-text. The user must still
    /// manually send the transcribed message.
    /// Requires a default speech-to-text deployment to be configured.
    /// </summary>
    AudioInput,

    /// <summary>
    /// Full conversation mode with two-way voice interaction.
    /// The user speaks, the transcript is sent directly as a prompt,
    /// the AI response is spoken back, and recording restarts automatically.
    /// Requires both speech-to-text and text-to-speech deployments.
    /// </summary>
    Conversation,

    /// <summary>
    /// Full speech-to-speech voice interaction over a provider realtime session, rather than the
    /// speech-to-text + chat + text-to-speech pipeline used by <see cref="Conversation"/>. Text turns still
    /// use the profile's orchestrator; voice turns run through the realtime execution engine.
    /// Requires a realtime deployment (per-profile or the site default).
    /// </summary>
    Realtime,
}
