using CrestApps.Core.AI.Documents.Pdf.Graphics;

namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A box of a set size that something draws itself into, such as a chart, kept on one page.
/// </summary>
internal sealed class PdfLayoutGraphic : PdfLayoutBlock
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfLayoutGraphic"/> class.
    /// </summary>
    /// <param name="width">The width, in points.</param>
    /// <param name="height">The height, in points.</param>
    /// <param name="draw">Draws into the box: given the canvas and the box's left and top edges.</param>
    public PdfLayoutGraphic(double width, double height, Action<PdfCanvas, double, double> draw)
    {
        Width = width;
        Height = height;
        Draw = draw;
    }

    /// <summary>
    /// Gets the width, in points.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Gets the height, in points.
    /// </summary>
    public double Height { get; }

    /// <summary>
    /// Gets what draws into the box.
    /// </summary>
    public Action<PdfCanvas, double, double> Draw { get; }

    /// <summary>
    /// Gets or sets where the box sits across the width available.
    /// </summary>
    public PdfLayoutAlignment Alignment { get; set; } = PdfLayoutAlignment.Center;

    /// <summary>
    /// Gets or sets the space above the box, in points.
    /// </summary>
    public double SpaceBefore { get; set; }

    /// <summary>
    /// Gets or sets the space below the box, in points.
    /// </summary>
    public double SpaceAfter { get; set; }

    /// <summary>
    /// Gets or sets whether the box is kept on the page of the block after it.
    /// </summary>
    public bool KeepWithNext { get; set; }
}
