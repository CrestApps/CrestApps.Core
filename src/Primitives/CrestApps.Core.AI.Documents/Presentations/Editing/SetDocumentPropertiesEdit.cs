namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Sets the document properties a file browser and PowerPoint's Info pane show.
/// </summary>
public sealed class SetDocumentPropertiesEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the subject.
    /// </summary>
    public string Subject { get; set; }

    /// <summary>
    /// Gets or sets the author.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets the keywords.
    /// </summary>
    public string Keywords { get; set; }
}
