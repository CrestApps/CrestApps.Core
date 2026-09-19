namespace CrestApps.Core.AI.Ingestion.Pdf.Services;

/// <summary>
/// One drawing a page builds out of vector geometry rather than placing as an image.
/// </summary>
internal sealed class DetectedVectorFigure
{
    /// <summary>
    /// Gets or sets the segments the drawing is made of.
    /// </summary>
    public IReadOnlyList<PdfSegment> Segments { get; set; } = [];

    /// <summary>
    /// Gets or sets the region the drawing occupies, as left, bottom, right and top in PDF user space.
    /// </summary>
    public double[] Bounds { get; set; } = [];
}
