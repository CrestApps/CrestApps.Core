namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// A slide background: a colour, a gradient or a picture.
/// </summary>
public sealed class PresentationBackgroundSpec
{
    /// <summary>
    /// Gets or sets a solid background colour.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets the colours of a gradient background, in order.
    /// </summary>
    public IList<string> GradientColors { get; set; }

    /// <summary>
    /// Gets or sets the direction of a gradient background, in degrees clockwise from left-to-right.
    /// </summary>
    public double? GradientAngle { get; set; }

    /// <summary>
    /// Gets or sets a picture that fills the background.
    /// </summary>
    public PresentationImageData Image { get; set; }

    /// <summary>
    /// Gets or sets how transparent a picture background is, from 0 (opaque) to 100 (invisible), so text
    /// stays readable over it.
    /// </summary>
    public double? ImageTransparency { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the background should go back to what the layout supplies.
    /// </summary>
    public bool Reset { get; set; }

    /// <summary>
    /// Gets a value indicating whether the specification sets a background.
    /// </summary>
    public bool IsEmpty => !Reset && Color is null && GradientColors is not { Count: > 0 } && Image is null;
}
