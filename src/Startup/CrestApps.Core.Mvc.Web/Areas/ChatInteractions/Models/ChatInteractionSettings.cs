using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Mvc.Web.Areas.ChatInteractions.Models;

public sealed class ChatInteractionSettings
{
    public ChatMode ChatMode { get; set; } = ChatMode.TextInput;

    public bool EnableUserMemory { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to show text-to-speech playback controls on
    /// assistant messages in Chat Interactions. Disabled by default.
    /// </summary>
    public bool EnableTextToSpeechPlayback { get; set; }
}
