namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// One thing drawn on a slide: a shape, a block of text or a picture.
/// </summary>
/// <remarks>
/// The drawing is what the preview and the PDF export both paint, item by item and back to front, so the two
/// cannot disagree about what a slide shows.
/// </remarks>
public abstract class SlideDrawingItem
{
    /// <summary>
    /// Gets or sets the clockwise rotation in degrees about <see cref="CenterX"/>, <see cref="CenterY"/>.
    /// </summary>
    public double Rotation { get; set; }

    /// <summary>
    /// Gets or sets the horizontal centre the item rotates and flips about, in points.
    /// </summary>
    public double CenterX { get; set; }

    /// <summary>
    /// Gets or sets the vertical centre the item rotates and flips about, in points.
    /// </summary>
    public double CenterY { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the item is mirrored left to right about its centre.
    /// </summary>
    public bool FlipHorizontal { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the item is mirrored top to bottom about its centre.
    /// </summary>
    public bool FlipVertical { get; set; }

    /// <summary>
    /// Gets a value indicating whether the item is transformed at all.
    /// </summary>
    public bool IsTransformed => Math.Abs(Rotation % 360) > 0.01 || FlipHorizontal || FlipVertical;
}
