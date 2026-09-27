namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// One element of a document's logical structure: a heading, a paragraph, a list item, a table or a figure.
/// </summary>
internal sealed class PdfStructureElement
{
    /// <summary>
    /// The type of a heading.
    /// </summary>
    public const string HeadingType = "heading";

    /// <summary>
    /// The type of a paragraph.
    /// </summary>
    public const string ParagraphType = "paragraph";

    /// <summary>
    /// The type of a list item.
    /// </summary>
    public const string ListItemType = "list_item";

    /// <summary>
    /// The type of a table.
    /// </summary>
    public const string TableType = "table";

    /// <summary>
    /// The type of a figure.
    /// </summary>
    public const string FigureType = "figure";

    /// <summary>
    /// The type of a caption found without the figure or table it names.
    /// </summary>
    public const string CaptionType = "caption";

    /// <summary>
    /// Gets the element type.
    /// </summary>
    public string Type { get; init; }

    /// <summary>
    /// Gets the one-based page the element is on, or starts on.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Gets a heading's level, one for the most prominent.
    /// </summary>
    public int? Level { get; init; }

    /// <summary>
    /// Gets the element's text: a heading's or paragraph's words, a list item's text after its marker.
    /// </summary>
    public string Text { get; init; }

    /// <summary>
    /// Gets a list item's marker, such as <c>•</c> or <c>2.</c>.
    /// </summary>
    public string Marker { get; init; }

    /// <summary>
    /// Gets the table or figure, for those elements.
    /// </summary>
    public PdfRegion Region { get; init; }

    /// <summary>
    /// Gets where the element is, as <c>[x, y, width, height]</c> in points from the top-left corner of the page.
    /// </summary>
    public double[] Box { get; init; }
}
