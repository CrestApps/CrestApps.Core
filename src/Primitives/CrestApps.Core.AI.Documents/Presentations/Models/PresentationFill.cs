namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// How a shape, a cell or a slide background is painted, after theme colours and inheritance are resolved.
/// </summary>
public sealed class PresentationFill
{
    /// <summary>
    /// A fill that paints nothing.
    /// </summary>
    public static PresentationFill None => new() { Kind = PresentationFillKind.None };

    /// <summary>
    /// Gets or sets how the area is painted.
    /// </summary>
    public PresentationFillKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the colour of a solid or pattern fill, as six hexadecimal digits without a leading hash.
    /// A gradient also sets it, to the colour of its first stop, so a reader that cannot draw gradients
    /// still has a colour to use.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets the opacity of a solid fill, from 0 (transparent) to 1 (opaque).
    /// </summary>
    public double Alpha { get; set; } = 1;

    /// <summary>
    /// Gets or sets the colours of a gradient, in order.
    /// </summary>
    public IList<PresentationGradientStop> Stops { get; set; } = [];

    /// <summary>
    /// Gets or sets the direction of a linear gradient, in degrees clockwise from left-to-right.
    /// </summary>
    public double GradientAngle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the gradient radiates from the centre rather than running in
    /// a straight line.
    /// </summary>
    public bool IsRadial { get; set; }

    /// <summary>
    /// Gets or sets the picture of a picture fill.
    /// </summary>
    public PresentationImage Image { get; set; }

    /// <summary>
    /// Gets a value indicating whether the fill paints anything.
    /// </summary>
    public bool IsVisible => Kind != PresentationFillKind.None && (Kind == PresentationFillKind.Picture || !string.IsNullOrEmpty(Color));

    /// <summary>
    /// Creates a solid fill.
    /// </summary>
    /// <param name="color">The colour as six hexadecimal digits.</param>
    /// <param name="alpha">The opacity, from 0 to 1.</param>
    /// <returns>The fill.</returns>
    public static PresentationFill Solid(string color, double alpha = 1)
    {
        return new PresentationFill
        {
            Kind = PresentationFillKind.Solid,
            Color = color,
            Alpha = alpha,
        };
    }
}
