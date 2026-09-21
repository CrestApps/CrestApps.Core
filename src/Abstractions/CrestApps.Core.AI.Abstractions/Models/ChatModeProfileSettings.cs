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
    /// <remarks>
    /// The chat mode decides how the conversation is carried. <see cref="ChatMode.Conversation"/> runs a
    /// speech-to-speech session when <see cref="ConversationDeploymentName"/> resolves through the realtime
    /// slot, and the client-driven speech-to-text plus text-to-speech cascade when it does not. The chat
    /// deployment means only "the text model this profile talks to" either way.
    /// </remarks>
    [System.Text.Json.Serialization.JsonConverter(typeof(ChatModeJsonConverter))]
    public ChatMode ChatMode { get; set; }

    /// <summary>
    /// Gets or sets the technical name of the deployment that carries a <see cref="ChatMode.Conversation"/>
    /// conversation. When <c>null</c> or empty, the realtime slot's own default applies -- the site's default
    /// realtime deployment, and failing that the first realtime-capable one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This names the model the conversation is carried by, not how it is carried. Whether the resolved
    /// deployment speaks natively or chains speech-to-text, chat, and text-to-speech together is read off the
    /// deployment's own <c>CascadedRealtimeMetadata</c>, so the profile never stores a transport choice the
    /// deployment could contradict.
    /// </para>
    /// <para>
    /// When nothing resolves, <see cref="ChatMode.Conversation"/> falls back to the client-driven
    /// speech-to-text plus text-to-speech conversation, which is what it has always been.
    /// </para>
    /// </remarks>
    public string ConversationDeploymentName { get; set; }

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
