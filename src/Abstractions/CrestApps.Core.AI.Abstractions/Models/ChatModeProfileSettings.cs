namespace CrestApps.Core.AI.Models;

/// <summary>
/// Settings stored on <see cref="AIProfile.Settings"/> to control the chat mode
/// and voice features for chat UIs using this profile.
/// </summary>
public sealed class ChatModeProfileSettings
{
    /// <summary>
    /// Gets or sets the chat mode for this profile.
    /// Defaults to <see cref="ChatMode.TextInput"/>.
    /// </summary>
    public ChatMode ChatMode { get; set; }

    /// <summary>
    /// Gets or sets the voice name to use for text-to-speech synthesis
    /// when the chat mode is <see cref="ChatMode.Conversation"/>.
    /// When <c>null</c> or empty, the provider's default voice is used.
    /// </summary>
    public string VoiceName { get; set; }

    /// <summary>
    /// Gets or sets whether to show text-to-speech playback controls on
    /// assistant messages. Disabled by default. When enabled the UI displays
    /// a play button on each assistant message, allowing the user to listen
    /// to the response via the configured TTS deployment.
    /// </summary>
    public bool EnableTextToSpeechPlayback { get; set; }
}
