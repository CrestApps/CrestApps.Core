using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Speech;

/// <summary>
/// One page of a voice search.
/// </summary>
public sealed class VoiceSearchResult
{
    /// <summary>
    /// Gets or sets the voices on this page.
    /// </summary>
    public IReadOnlyList<SpeechVoice> Voices { get; set; } = [];

    /// <summary>
    /// Gets or sets the cursor of the next page, or <see langword="null"/> when this was the last one.
    /// </summary>
    public string NextCursor { get; set; }
}
