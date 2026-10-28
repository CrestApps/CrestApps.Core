namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A paragraph laid out into lines for a width: what placing it on pages needs.
/// </summary>
internal sealed class PdfLaidParagraph
{
    /// <summary>
    /// Gets or sets the paragraph's format, with everything its style gives.
    /// </summary>
    public PdfLayoutParagraphFormat Format { get; set; }

    /// <summary>
    /// Gets the lines.
    /// </summary>
    public List<PdfLaidLine> Lines { get; } = [];

    /// <summary>
    /// Gets or sets the space above the paragraph, in points.
    /// </summary>
    public double SpaceBefore { get; set; }

    /// <summary>
    /// Gets or sets the space below the paragraph, in points.
    /// </summary>
    public double SpaceAfter { get; set; }

    /// <summary>
    /// Gets or sets the room the top border takes above the first line, in points.
    /// </summary>
    public double TopInset { get; set; }

    /// <summary>
    /// Gets or sets the room the bottom border takes below the last line, in points.
    /// </summary>
    public double BottomInset { get; set; }

    /// <summary>
    /// Gets or sets the paragraph's text, without fields' placeholders, for the document outline.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets the height of the lines, without the spacing and the borders.
    /// </summary>
    public double LinesHeight => Lines.Sum(line => line.Height);

    /// <summary>
    /// Gets the height of the whole paragraph, spacing and borders included.
    /// </summary>
    public double Height => SpaceBefore + TopInset + LinesHeight + BottomInset + SpaceAfter;
}
