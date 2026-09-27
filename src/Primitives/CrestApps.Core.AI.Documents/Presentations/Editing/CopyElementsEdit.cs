namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Copies or moves elements to another slide, or copies them on the same slide, keeping their formatting and
/// the pictures and charts they hold.
/// </summary>
public sealed class CopyElementsEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the number of the slide the elements are on.
    /// </summary>
    public int Slide { get; set; }

    /// <summary>
    /// Gets or sets the elements, by identifier, name or placeholder role.
    /// </summary>
    public IList<string> Elements { get; set; } = [];

    /// <summary>
    /// Gets or sets the number of the slide the elements go to.
    /// </summary>
    public int TargetSlide { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the elements are removed from the source slide.
    /// </summary>
    public bool Move { get; set; }

    /// <summary>
    /// Gets or sets how far to shift the copies to the right.
    /// </summary>
    public PresentationLength? OffsetX { get; set; }

    /// <summary>
    /// Gets or sets how far to shift the copies down.
    /// </summary>
    public PresentationLength? OffsetY { get; set; }
}
