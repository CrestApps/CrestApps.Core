namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// How much of a picture to cut away from each edge, as percentages of the picture, without discarding the
/// picture underneath.
/// </summary>
public sealed class PresentationCropSpec
{
    /// <summary>
    /// Gets or sets the percentage cut off the left edge.
    /// </summary>
    public double Left { get; set; }

    /// <summary>
    /// Gets or sets the percentage cut off the top edge.
    /// </summary>
    public double Top { get; set; }

    /// <summary>
    /// Gets or sets the percentage cut off the right edge.
    /// </summary>
    public double Right { get; set; }

    /// <summary>
    /// Gets or sets the percentage cut off the bottom edge.
    /// </summary>
    public double Bottom { get; set; }
}
