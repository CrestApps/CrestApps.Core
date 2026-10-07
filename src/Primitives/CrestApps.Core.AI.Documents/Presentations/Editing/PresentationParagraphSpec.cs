namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// A paragraph of text to write: its words, its outline level, its bullet and its style.
/// </summary>
public sealed class PresentationParagraphSpec
{
    /// <summary>
    /// Gets or sets the runs of text, each with its own style or link. A paragraph written from plain text
    /// has a single run.
    /// </summary>
    public IList<PresentationRunSpec> Runs { get; set; } = [];

    /// <summary>
    /// Gets or sets the outline level, from 0 (top level) to 8.
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Gets or sets the bullet: <see langword="null"/> to keep what the placeholder supplies, <c>none</c>,
    /// <c>bullet</c>, <c>number</c>, <c>letter</c>, <c>roman</c>, or a single character to use as the
    /// marker.
    /// </summary>
    public string Bullet { get; set; }

    /// <summary>
    /// Gets or sets the style of the paragraph's text and its alignment and spacing.
    /// </summary>
    public PresentationTextStyle Style { get; set; }

    /// <summary>
    /// Gets the paragraph's plain text.
    /// </summary>
    public string Text => string.Concat(Runs.Select(run => run.Text));

    /// <summary>
    /// Creates a paragraph from plain text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="level">The outline level, from 0.</param>
    /// <returns>The paragraph.</returns>
    public static PresentationParagraphSpec FromText(string text, int level = 0)
    {
        return new PresentationParagraphSpec
        {
            Runs = [new PresentationRunSpec { Text = text ?? string.Empty }],
            Level = level,
        };
    }
}
