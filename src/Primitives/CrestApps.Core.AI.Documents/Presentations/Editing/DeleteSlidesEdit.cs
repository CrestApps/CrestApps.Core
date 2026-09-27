namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Removes one or more slides.
/// </summary>
public sealed class DeleteSlidesEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the numbers of the slides to remove, as the deck numbers them before any is removed.
    /// </summary>
    public IList<int> Slides { get; set; } = [];
}
