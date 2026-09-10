using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Mvc.Web.Areas.ChatInteractions.Models;

public sealed class ChatInteractionSettings
{
    // Realtime is no longer a chat mode; it follows from the selected deployment declaring the realtime
    // capability. The converter keeps sites written before that change loadable.
    [System.Text.Json.Serialization.JsonConverter(typeof(ChatModeJsonConverter))]
    public ChatMode ChatMode { get; set; } = ChatMode.TextInput;

    public bool EnableUserMemory { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to show text-to-speech playback controls on
    /// assistant messages in Chat Interactions. Disabled by default.
    /// </summary>
    public bool EnableTextToSpeechPlayback { get; set; }
}
