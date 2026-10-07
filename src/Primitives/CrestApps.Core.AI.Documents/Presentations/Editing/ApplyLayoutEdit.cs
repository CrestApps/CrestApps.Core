namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Rebuilds slides on a different layout, moving their placeholder content into the new layout's
/// placeholders.
/// </summary>
public sealed class ApplyLayoutEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the numbers of the slides to change.
    /// </summary>
    public IList<int> Slides { get; set; } = [];

    /// <summary>
    /// Gets or sets the layout, by name or by kind.
    /// </summary>
    public string Layout { get; set; }
}
