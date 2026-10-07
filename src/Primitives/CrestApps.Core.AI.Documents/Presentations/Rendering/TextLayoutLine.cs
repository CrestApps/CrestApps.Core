namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// One line of laid-out text, positioned in points from the top-left corner of the slide.
/// </summary>
internal sealed class TextLayoutLine
{
    /// <summary>
    /// Gets or sets the horizontal position the line is anchored at: its left edge, centre or right edge
    /// depending on <see cref="Anchor"/>.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Gets or sets the vertical position of the line's baseline.
    /// </summary>
    public double Baseline { get; set; }

    /// <summary>
    /// Gets or sets how the line is anchored at <see cref="X"/>: <c>start</c>, <c>middle</c> or <c>end</c>.
    /// </summary>
    public string Anchor { get; set; } = "start";

    /// <summary>
    /// Gets or sets the estimated width of the line in points.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Gets or sets the height the line takes, in points.
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Gets or sets the pieces of the line, left to right.
    /// </summary>
    public IList<TextLayoutSpan> Spans { get; set; } = [];

    /// <summary>
    /// Gets or sets the bullet or number drawn in front of the line, for the first line of a paragraph that
    /// has one.
    /// </summary>
    public TextLayoutSpan Marker { get; set; }

    /// <summary>
    /// Gets or sets the horizontal position of <see cref="Marker"/>.
    /// </summary>
    public double MarkerX { get; set; }

    /// <summary>
    /// Gets the left edge of the line, whatever its anchor.
    /// </summary>
    public double Left => Anchor switch
    {
        "middle" => X - (Width / 2),
        "end" => X - Width,
        _ => X,
    };
}
