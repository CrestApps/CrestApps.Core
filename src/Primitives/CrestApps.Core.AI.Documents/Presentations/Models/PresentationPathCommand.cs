namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// One drawing command of a freeform outline, with its points given as fractions of the shape's width and
/// height so the outline scales with the shape.
/// </summary>
public sealed class PresentationPathCommand
{
    /// <summary>
    /// Gets or sets the command: <c>M</c> moves to a point, <c>L</c> draws a line to it, <c>C</c> draws a
    /// cubic curve through two control points to a third, and <c>Z</c> closes the outline.
    /// </summary>
    public char Kind { get; set; }

    /// <summary>
    /// Gets or sets the points, as fractions of the shape's width (X) and height (Y).
    /// </summary>
    public IList<(double X, double Y)> Points { get; set; } = [];
}
