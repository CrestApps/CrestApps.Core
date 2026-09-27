namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// The text inside a shape or a table cell, with the box it is laid out in.
/// </summary>
public sealed class PresentationTextBody
{
    /// <summary>
    /// Gets or sets the paragraphs, in order.
    /// </summary>
    public IList<PresentationParagraph> Paragraphs { get; set; } = [];

    /// <summary>
    /// Gets or sets where the text sits vertically in its box: <c>top</c>, <c>middle</c> or <c>bottom</c>.
    /// </summary>
    public string VerticalAnchor { get; set; } = "top";

    /// <summary>
    /// Gets or sets the gap between the left edge of the shape and the text, in EMUs.
    /// </summary>
    public long InsetLeft { get; set; } = 91_440;

    /// <summary>
    /// Gets or sets the gap between the top edge of the shape and the text, in EMUs.
    /// </summary>
    public long InsetTop { get; set; } = 45_720;

    /// <summary>
    /// Gets or sets the gap between the right edge of the shape and the text, in EMUs.
    /// </summary>
    public long InsetRight { get; set; } = 91_440;

    /// <summary>
    /// Gets or sets the gap between the bottom edge of the shape and the text, in EMUs.
    /// </summary>
    public long InsetBottom { get; set; } = 45_720;

    /// <summary>
    /// Gets or sets a value indicating whether lines wrap at the edge of the box. When they do not, each
    /// paragraph is one line however long it is.
    /// </summary>
    public bool Wrap { get; set; } = true;

    /// <summary>
    /// Gets or sets what happens when the text does not fit: <c>none</c> lets it overflow, <c>shrink</c>
    /// shrinks it to fit, and <c>resize</c> grows the shape to fit it.
    /// </summary>
    public string AutoFit { get; set; } = "none";

    /// <summary>
    /// Gets or sets the scale the application last applied to shrink the text to fit, where 1 is full size.
    /// It is already applied to every run's size.
    /// </summary>
    public double FontScale { get; set; } = 1;

    /// <summary>
    /// Gets or sets the fraction the application last took off the line spacing to fit the text, from 0 to
    /// 1. Unlike <see cref="FontScale"/> it is not applied to the paragraphs; layout applies it.
    /// </summary>
    public double LineSpacingReduction { get; set; }

    /// <summary>
    /// Gets or sets the text direction: <c>horizontal</c>, or <c>vertical</c> for text rotated a quarter
    /// turn.
    /// </summary>
    public string Direction { get; set; } = "horizontal";

    /// <summary>
    /// Gets or sets the number of text columns.
    /// </summary>
    public int Columns { get; set; } = 1;

    /// <summary>
    /// Gets a value indicating whether the body holds any visible text.
    /// </summary>
    public bool HasText => Paragraphs.Any(paragraph => !string.IsNullOrWhiteSpace(paragraph.Text));

    /// <summary>
    /// Gets the plain text of the body, one paragraph per line.
    /// </summary>
    public string PlainText => string.Join('\n', Paragraphs.Select(paragraph => paragraph.Text));
}
