namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// A shape on a drawn slide: one or more outlines, a fill and a stroke.
/// </summary>
public sealed class SlideShapeDrawing : SlideDrawingItem
{
    /// <summary>
    /// Gets or sets the outlines.
    /// </summary>
    public IList<SlidePath> Paths { get; set; } = [];

    /// <summary>
    /// Gets or sets the fill, or <see langword="null"/> for none.
    /// </summary>
    public SlidePaint Fill { get; set; }

    /// <summary>
    /// Gets or sets the stroke, or <see langword="null"/> for none.
    /// </summary>
    public SlideStroke Stroke { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the shape casts a soft shadow.
    /// </summary>
    public bool Shadow { get; set; }

    /// <summary>
    /// Gets or sets the box the shape occupies, in points, which a picture fill is fitted to.
    /// </summary>
    public (double X, double Y, double Width, double Height) Box { get; set; }
}
