namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// One colour on a gradient.
/// </summary>
public sealed class PresentationGradientStop
{
    /// <summary>
    /// Gets or sets how far along the gradient the colour sits, from 0 (the start) to 1 (the end).
    /// </summary>
    public double Position { get; set; }

    /// <summary>
    /// Gets or sets the colour as six hexadecimal digits, without a leading hash.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets the opacity, from 0 (transparent) to 1 (opaque).
    /// </summary>
    public double Alpha { get; set; } = 1;
}
