using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// How a drawn shape is filled.
/// </summary>
public sealed class SlidePaint
{
    /// <summary>
    /// Gets or sets the colour of a solid fill, as six hexadecimal digits.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets the opacity, from 0 to 1.
    /// </summary>
    public double Alpha { get; set; } = 1;

    /// <summary>
    /// Gets or sets the stops of a gradient fill; empty for a solid fill.
    /// </summary>
    public IList<PresentationGradientStop> Stops { get; set; } = [];

    /// <summary>
    /// Gets or sets the direction of a linear gradient, in degrees clockwise from left-to-right.
    /// </summary>
    public double Angle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the gradient radiates from the centre.
    /// </summary>
    public bool Radial { get; set; }

    /// <summary>
    /// Gets or sets a picture that fills the shape.
    /// </summary>
    public PresentationImage Image { get; set; }

    /// <summary>
    /// Creates a solid paint.
    /// </summary>
    /// <param name="color">The colour.</param>
    /// <param name="alpha">The opacity.</param>
    /// <returns>The paint.</returns>
    public static SlidePaint Solid(string color, double alpha = 1)
    {
        return new SlidePaint { Color = color, Alpha = alpha };
    }
}
