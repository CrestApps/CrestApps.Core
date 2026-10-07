namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// How a drawn outline or line is stroked.
/// </summary>
public sealed class SlideStroke
{
    /// <summary>
    /// Gets or sets the colour, as six hexadecimal digits.
    /// </summary>
    public string Color { get; set; } = "000000";

    /// <summary>
    /// Gets or sets the opacity, from 0 to 1.
    /// </summary>
    public double Alpha { get; set; } = 1;

    /// <summary>
    /// Gets or sets the width in points.
    /// </summary>
    public double Width { get; set; } = 1;

    /// <summary>
    /// Gets or sets the dash pattern: <c>solid</c>, <c>dash</c>, <c>dot</c>, <c>dash_dot</c>,
    /// <c>long_dash</c> or <c>long_dash_dot</c>.
    /// </summary>
    public string Dash { get; set; } = "solid";

    /// <summary>
    /// Gets or sets the arrowhead at the start of a line.
    /// </summary>
    public string StartArrow { get; set; } = "none";

    /// <summary>
    /// Gets or sets the arrowhead at the end of a line.
    /// </summary>
    public string EndArrow { get; set; } = "none";
}
