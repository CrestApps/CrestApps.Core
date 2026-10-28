namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// The size and margins of a section's pages, in points.
/// </summary>
internal sealed class PdfLayoutPageSetup
{
    /// <summary>
    /// Gets or sets the page width.
    /// </summary>
    public double PageWidth { get; set; } = 595.276;

    /// <summary>
    /// Gets or sets the page height.
    /// </summary>
    public double PageHeight { get; set; } = 841.89;

    /// <summary>
    /// Gets or sets the top margin, above the text.
    /// </summary>
    public double TopMargin { get; set; } = 72;

    /// <summary>
    /// Gets or sets the bottom margin, below the text.
    /// </summary>
    public double BottomMargin { get; set; } = 72;

    /// <summary>
    /// Gets or sets the left margin.
    /// </summary>
    public double LeftMargin { get; set; } = 72;

    /// <summary>
    /// Gets or sets the right margin.
    /// </summary>
    public double RightMargin { get; set; } = 72;

    /// <summary>
    /// Gets or sets how far the header starts from the top edge.
    /// </summary>
    public double HeaderDistance { get; set; } = 36;

    /// <summary>
    /// Gets or sets how far the footer ends from the bottom edge.
    /// </summary>
    public double FooterDistance { get; set; } = 36;

    /// <summary>
    /// Gets or sets the number the section's first page gets, or <see langword="null"/> to go on from the
    /// section before.
    /// </summary>
    public int? StartingNumber { get; set; }

    /// <summary>
    /// Gets or sets whether the section's first page has a header and footer of its own.
    /// </summary>
    public bool DifferentFirstPageHeaderFooter { get; set; }

    /// <summary>
    /// Gets the width between the margins.
    /// </summary>
    public double UsableWidth => PageWidth - LeftMargin - RightMargin;
}
