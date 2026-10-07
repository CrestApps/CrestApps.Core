namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// One line of text on a drawn slide.
/// </summary>
public sealed class SlideTextLine
{
    /// <summary>
    /// Gets or sets the horizontal position the line is anchored at, in points.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Gets or sets the baseline, in points.
    /// </summary>
    public double Baseline { get; set; }

    /// <summary>
    /// Gets or sets how the line is anchored at <see cref="X"/>: <c>start</c>, <c>middle</c> or <c>end</c>.
    /// </summary>
    public string Anchor { get; set; } = "start";

    /// <summary>
    /// Gets or sets the estimated width of the line in points, which an underline or highlight spans.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Gets or sets the runs of the line, left to right.
    /// </summary>
    public IList<SlideTextRun> Runs { get; set; } = [];

    /// <summary>
    /// Gets or sets the bullet or number in front of the line, when it has one.
    /// </summary>
    public SlideTextRun Marker { get; set; }

    /// <summary>
    /// Gets or sets the horizontal position of <see cref="Marker"/>.
    /// </summary>
    public double MarkerX { get; set; }
}
