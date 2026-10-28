namespace CrestApps.Core.AI.Models;

/// <summary>
/// Maps a word or phrase to the text a text-to-speech voice should be given in its place, so one dictionary works with
/// every provider.
/// </summary>
public sealed class PronunciationEntry
{
    /// <summary>
    /// Gets or sets the word or phrase to replace.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the spoken alias, spelled the way it should sound (for example <c>sequel</c> for <c>SQL</c>).
    /// </summary>
    public string Alias { get; set; }

    /// <summary>
    /// Gets or sets whether the match is case-sensitive. Defaults to <see langword="false"/>.
    /// </summary>
    public bool MatchCase { get; set; }
}
