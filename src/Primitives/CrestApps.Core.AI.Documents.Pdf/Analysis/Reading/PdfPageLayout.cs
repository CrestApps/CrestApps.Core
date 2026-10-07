namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// The layout of one page: its blocks in reading order, its columns, figures and tables.
/// </summary>
internal sealed class PdfPageLayout
{
    /// <summary>
    /// Gets the one-based page number.
    /// </summary>
    public int PageNumber { get; init; }

    /// <summary>
    /// Gets the page's visible area, in user space.
    /// </summary>
    public PdfBox Visible { get; init; }

    /// <summary>
    /// Gets the page rotation, in degrees.
    /// </summary>
    public int Rotation { get; init; }

    /// <summary>
    /// Gets the text blocks, in reading order.
    /// </summary>
    public List<PdfLayoutBlock> Blocks { get; } = [];

    /// <summary>
    /// Gets the placed pictures and drawings.
    /// </summary>
    public List<PdfRegion> Figures { get; } = [];

    /// <summary>
    /// Gets the tables.
    /// </summary>
    public List<PdfRegion> Tables { get; } = [];

    /// <summary>
    /// Gets the columns of text, left to right, as their left and right edges in user space.
    /// </summary>
    public List<(double Left, double Right)> Columns { get; } = [];

    /// <summary>
    /// Describes a box on this page in top-left coordinates.
    /// </summary>
    /// <param name="box">The box in user space.</param>
    /// <returns>For example <c>x=72, y=120.5, w=80, h=11</c>.</returns>
    public string Describe(PdfBox box)
    {
        return PdfBoxes.Describe(box, Visible);
    }
}
