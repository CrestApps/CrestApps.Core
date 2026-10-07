namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Groups elements so they move and resize together, or breaks a group back into its elements.
/// </summary>
public sealed class GroupElementsEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the number of the slide.
    /// </summary>
    public int Slide { get; set; }

    /// <summary>
    /// Gets or sets the elements to group, or the single group to break apart.
    /// </summary>
    public IList<string> Elements { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the edit breaks a group apart rather than making one.
    /// </summary>
    public bool Ungroup { get; set; }

    /// <summary>
    /// Gets or sets the name of the new group.
    /// </summary>
    public string Name { get; set; }
}
