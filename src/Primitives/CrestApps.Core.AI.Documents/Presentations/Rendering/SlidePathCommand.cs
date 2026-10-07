namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// One command of an outline on a drawn slide, in points from the slide's top-left corner.
/// </summary>
/// <param name="Kind"><c>M</c> moves, <c>L</c> draws a line, <c>C</c> draws a cubic curve and <c>Z</c> closes the outline.</param>
/// <param name="Points">The points: one for <c>M</c> and <c>L</c>, three for <c>C</c>, none for <c>Z</c>.</param>
public readonly record struct SlidePathCommand(char Kind, (double X, double Y)[] Points)
{
    /// <summary>
    /// Creates a move.
    /// </summary>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <returns>The command.</returns>
    public static SlidePathCommand MoveTo(double x, double y)
    {
        return new SlidePathCommand('M', [(x, y)]);
    }

    /// <summary>
    /// Creates a line.
    /// </summary>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <returns>The command.</returns>
    public static SlidePathCommand LineTo(double x, double y)
    {
        return new SlidePathCommand('L', [(x, y)]);
    }

    /// <summary>
    /// Creates a cubic curve.
    /// </summary>
    /// <param name="x1">The first control point's horizontal position.</param>
    /// <param name="y1">The first control point's vertical position.</param>
    /// <param name="x2">The second control point's horizontal position.</param>
    /// <param name="y2">The second control point's vertical position.</param>
    /// <param name="x">The end point's horizontal position.</param>
    /// <param name="y">The end point's vertical position.</param>
    /// <returns>The command.</returns>
    public static SlidePathCommand CurveTo(double x1, double y1, double x2, double y2, double x, double y)
    {
        return new SlidePathCommand('C', [(x1, y1), (x2, y2), (x, y)]);
    }

    /// <summary>
    /// Creates a close.
    /// </summary>
    /// <returns>The command.</returns>
    public static SlidePathCommand Close()
    {
        return new SlidePathCommand('Z', []);
    }
}
