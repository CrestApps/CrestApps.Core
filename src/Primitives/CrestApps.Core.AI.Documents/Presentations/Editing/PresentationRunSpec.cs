namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// A stretch of text with its own style or link, inside a paragraph.
/// </summary>
public sealed class PresentationRunSpec
{
    /// <summary>
    /// Gets or sets the text.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the style of this run, laid over the paragraph's.
    /// </summary>
    public PresentationTextStyle Style { get; set; }

    /// <summary>
    /// Gets or sets where clicking the run goes.
    /// </summary>
    public PresentationLinkSpec Link { get; set; }
}
