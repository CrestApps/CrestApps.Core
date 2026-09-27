namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// One outline of a drawn shape.
/// </summary>
public sealed class SlidePath
{
    /// <summary>
    /// Gets or sets the commands, in points from the slide's top-left corner.
    /// </summary>
    public IList<SlidePathCommand> Commands { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the outline is filled with the shape's fill.
    /// </summary>
    public bool Filled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the outline is stroked with the shape's outline.
    /// </summary>
    public bool Stroked { get; set; } = true;

    /// <summary>
    /// Gets or sets how much darker (positive) or lighter (negative) than the shape's fill this outline is
    /// painted, as the side of a cube or the rim of a cylinder is.
    /// </summary>
    public double Shade { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether overlapping outlines cut holes in each other, as the hole of a
    /// ring does.
    /// </summary>
    public bool EvenOdd { get; set; }
}
