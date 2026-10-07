namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Replaces the deck's sections with a new set. An empty set removes every section.
/// </summary>
public sealed class UpdateSectionsEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the sections, in any order; they are sorted by the slide each starts at.
    /// </summary>
    public IList<PresentationSectionSpec> Sections { get; set; } = [];
}
