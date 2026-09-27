namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// The outline of a shape, the stroke of a line, or the border of a table cell.
/// </summary>
public sealed class PresentationLine
{
    /// <summary>
    /// Gets or sets the colour as six hexadecimal digits without a leading hash, or <see langword="null"/>
    /// when no line is drawn.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets the opacity, from 0 (transparent) to 1 (opaque).
    /// </summary>
    public double Alpha { get; set; } = 1;

    /// <summary>
    /// Gets or sets the thickness in points.
    /// </summary>
    public double Width { get; set; } = 0.75;

    /// <summary>
    /// Gets or sets the dash pattern: <c>solid</c>, <c>dash</c>, <c>dot</c>, <c>dash_dot</c>,
    /// <c>long_dash</c> or <c>long_dash_dot</c>.
    /// </summary>
    public string Dash { get; set; } = "solid";

    /// <summary>
    /// Gets or sets the arrowhead at the start of a line: <c>none</c>, <c>triangle</c>, <c>arrow</c>,
    /// <c>stealth</c>, <c>diamond</c> or <c>oval</c>.
    /// </summary>
    public string StartArrow { get; set; } = "none";

    /// <summary>
    /// Gets or sets the arrowhead at the end of a line, from the same set as <see cref="StartArrow"/>.
    /// </summary>
    public string EndArrow { get; set; } = "none";

    /// <summary>
    /// Gets a value indicating whether a line is drawn at all.
    /// </summary>
    public bool IsVisible => !string.IsNullOrEmpty(Color) && Width > 0;
}
