namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Removes elements from a slide.
/// </summary>
public sealed class DeleteElementsEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the number of the slide.
    /// </summary>
    public int Slide { get; set; }

    /// <summary>
    /// Gets or sets the elements to remove, by identifier, name or placeholder role.
    /// </summary>
    public IList<string> Elements { get; set; } = [];
}
