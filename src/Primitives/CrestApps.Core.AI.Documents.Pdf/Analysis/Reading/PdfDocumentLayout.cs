namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// The layout of the pages of a document that were analysed, with the type sizes that set its body text
/// and its headings apart.
/// </summary>
internal sealed class PdfDocumentLayout
{
    /// <summary>
    /// Gets the pages, in the order they were analysed.
    /// </summary>
    public List<PdfPageLayout> Pages { get; } = [];

    /// <summary>
    /// Gets or sets the size most of the body text is set in, in points.
    /// </summary>
    public double BodyFontSize { get; set; }

    /// <summary>
    /// Gets the sizes headings are set in, largest first; the first is level one.
    /// </summary>
    public List<double> HeadingSizes { get; } = [];
}
