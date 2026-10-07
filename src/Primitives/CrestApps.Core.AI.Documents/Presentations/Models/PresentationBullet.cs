namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// The marker drawn in front of a paragraph.
/// </summary>
public sealed class PresentationBullet
{
    /// <summary>
    /// Gets or sets the kind of marker: <c>none</c>, <c>char</c> for a symbol, <c>number</c> for automatic
    /// numbering, or <c>picture</c> for an image bullet.
    /// </summary>
    public string Kind { get; set; } = "none";

    /// <summary>
    /// Gets or sets the symbol of a <c>char</c> bullet.
    /// </summary>
    public string Character { get; set; }

    /// <summary>
    /// Gets or sets the numbering scheme of a <c>number</c> bullet, as the package names it, for example
    /// <c>arabicPeriod</c> or <c>alphaLcParenR</c>.
    /// </summary>
    public string NumberScheme { get; set; }

    /// <summary>
    /// Gets or sets the number the first paragraph of a numbered list starts at.
    /// </summary>
    public int StartAt { get; set; } = 1;

    /// <summary>
    /// Gets or sets the colour of the marker, when it differs from the text.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets the typeface the symbol is drawn in, when it differs from the text.
    /// </summary>
    public string Font { get; set; }

    /// <summary>
    /// Gets or sets the size of the marker relative to the text, where 1 is the same size.
    /// </summary>
    public double SizeRatio { get; set; } = 1;

    /// <summary>
    /// Gets a value indicating whether a marker is drawn.
    /// </summary>
    public bool IsVisible => Kind is "char" or "number" or "picture";
}
