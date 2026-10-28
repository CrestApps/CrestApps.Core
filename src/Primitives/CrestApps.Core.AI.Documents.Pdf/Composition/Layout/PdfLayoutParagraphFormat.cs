namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// How a paragraph is set: its font, alignment, spacing, indents, borders, shading and tab stops. A value
/// left unset is taken from the paragraph's style, and from the style that one is based on.
/// </summary>
internal sealed class PdfLayoutParagraphFormat
{
    /// <summary>
    /// Gets or sets the font.
    /// </summary>
    public PdfLayoutFont Font { get; set; } = new();

    /// <summary>
    /// Gets or sets the alignment of the lines.
    /// </summary>
    public PdfLayoutAlignment? Alignment { get; set; }

    /// <summary>
    /// Gets or sets the space above the paragraph, in points.
    /// </summary>
    public double? SpaceBefore { get; set; }

    /// <summary>
    /// Gets or sets the space below the paragraph, in points.
    /// </summary>
    public double? SpaceAfter { get; set; }

    /// <summary>
    /// Gets or sets how the height of the lines is set.
    /// </summary>
    public PdfLayoutLineSpacing? LineSpacingRule { get; set; }

    /// <summary>
    /// Gets or sets the line spacing: a factor for <see cref="PdfLayoutLineSpacing.Multiple"/>, points for
    /// <see cref="PdfLayoutLineSpacing.Exactly"/>.
    /// </summary>
    public double? LineSpacing { get; set; }

    /// <summary>
    /// Gets or sets the indent of every line from the left, in points.
    /// </summary>
    public double? LeftIndent { get; set; }

    /// <summary>
    /// Gets or sets the indent of every line from the right, in points.
    /// </summary>
    public double? RightIndent { get; set; }

    /// <summary>
    /// Gets or sets how much further the first line is indented than the others, in points; negative hangs it.
    /// </summary>
    public double? FirstLineIndent { get; set; }

    /// <summary>
    /// Gets or sets whether the paragraph is kept on the page of the paragraph after it.
    /// </summary>
    public bool? KeepWithNext { get; set; }

    /// <summary>
    /// Gets or sets the level of the paragraph in the document outline, 1 to 9, or 0 for body text.
    /// </summary>
    public int? OutlineLevel { get; set; }

    /// <summary>
    /// Gets or sets the background colour behind the paragraph.
    /// </summary>
    public PdfColor? Shading { get; set; }

    /// <summary>
    /// Gets or sets the borders.
    /// </summary>
    public PdfLayoutBorders Borders { get; set; } = new();

    /// <summary>
    /// Gets or sets the tab stops, or <see langword="null"/> to take the style's.
    /// </summary>
    public List<PdfLayoutTabStop> TabStops { get; set; }

    /// <summary>
    /// Returns this format with the values it leaves unset taken from another.
    /// </summary>
    /// <param name="fallback">The format the unset values come from.</param>
    /// <returns>The combined format.</returns>
    public PdfLayoutParagraphFormat Over(PdfLayoutParagraphFormat fallback)
    {
        if (fallback is null)
        {
            return this;
        }

        return new PdfLayoutParagraphFormat
        {
            Font = Font.Over(fallback.Font),
            Alignment = Alignment ?? fallback.Alignment,
            SpaceBefore = SpaceBefore ?? fallback.SpaceBefore,
            SpaceAfter = SpaceAfter ?? fallback.SpaceAfter,
            LineSpacingRule = LineSpacingRule ?? fallback.LineSpacingRule,
            LineSpacing = LineSpacing ?? fallback.LineSpacing,
            LeftIndent = LeftIndent ?? fallback.LeftIndent,
            RightIndent = RightIndent ?? fallback.RightIndent,
            FirstLineIndent = FirstLineIndent ?? fallback.FirstLineIndent,
            KeepWithNext = KeepWithNext ?? fallback.KeepWithNext,
            OutlineLevel = OutlineLevel ?? fallback.OutlineLevel,
            Shading = Shading ?? fallback.Shading,
            Borders = Borders.Over(fallback.Borders),
            TabStops = TabStops ?? fallback.TabStops,
        };
    }
}
