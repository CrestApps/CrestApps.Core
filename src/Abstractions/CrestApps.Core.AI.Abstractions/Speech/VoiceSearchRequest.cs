using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Speech;

/// <summary>
/// Describes a search of a speech provider's voice library.
/// </summary>
public sealed class VoiceSearchRequest
{
    /// <summary>
    /// Gets or sets text matched against the voices' names and descriptions, or <see langword="null"/> for every voice.
    /// </summary>
    public string Query { get; set; }

    /// <summary>
    /// Gets or sets the language the voice should speak, as a BCP-47 tag (for example <c>es</c> or <c>es-MX</c>), or
    /// <see langword="null"/> for any.
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    /// Gets or sets the gender of the voice, or <see langword="null"/> for any.
    /// </summary>
    public SpeechVoiceGender? Gender { get; set; }

    /// <summary>
    /// Gets or sets how many voices to return at most. Defaults to 25.
    /// </summary>
    public int PageSize { get; set; } = 25;

    /// <summary>
    /// Gets or sets the <see cref="VoiceSearchResult.NextCursor"/> of the previous page, or <see langword="null"/> for
    /// the first page.
    /// </summary>
    public string Cursor { get; set; }
}
