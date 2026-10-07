namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Sets the background of slides, or of the slide master so every slide that does not override it follows.
/// </summary>
public sealed class SetBackgroundEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the numbers of the slides to change. Every slide is changed when the list is empty and
    /// <see cref="ApplyToMaster"/> is not set.
    /// </summary>
    public IList<int> Slides { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the background is set on the slide masters, and cleared from
    /// slides that override it, rather than on the slides one by one.
    /// </summary>
    public bool ApplyToMaster { get; set; }

    /// <summary>
    /// Gets or sets the background.
    /// </summary>
    public PresentationBackgroundSpec Background { get; set; }
}
