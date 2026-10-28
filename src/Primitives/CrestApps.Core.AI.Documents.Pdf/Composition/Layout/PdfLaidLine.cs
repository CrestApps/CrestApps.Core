namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A laid-out line of a paragraph.
/// </summary>
internal sealed class PdfLaidLine
{
    /// <summary>
    /// Gets what the line holds, left to right.
    /// </summary>
    public List<PdfLaidItem> Items { get; } = [];

    /// <summary>
    /// Gets or sets where the line starts, in points from the left edge of the paragraph's area.
    /// </summary>
    public double Left { get; set; }

    /// <summary>
    /// Gets or sets the line's height, in points.
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Gets or sets how far the baseline is below the line's top, in points.
    /// </summary>
    public double Baseline { get; set; }

    /// <summary>
    /// Gets the bookmarks the line holds.
    /// </summary>
    public List<string> Bookmarks { get; } = [];
}
