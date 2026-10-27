using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Speech;

/// <summary>
/// Describes a caller language detected during a conversation.
/// </summary>
public sealed class DetectedLanguageContext
{
    /// <summary>
    /// Gets or sets the profile the conversation belongs to.
    /// </summary>
    public AIProfile Profile { get; set; }

    /// <summary>
    /// Gets or sets the conversation's session id, when it has one.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the language the conversation was in, or <see langword="null"/> when none was set.
    /// </summary>
    public string PreviousLanguage { get; set; }

    /// <summary>
    /// Gets or sets the detected language as a BCP-47 tag, normalized to the profile's allowed spelling.
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    /// Gets or sets the voice a handler wants the conversation to speak with from now on.
    /// </summary>
    public string VoiceName { get; set; }
}
