namespace CrestApps.Core.AI.Documents.Word.Workspace;

/// <summary>
/// Everything a conversation's Word workspace holds between turns.
/// </summary>
internal sealed class WordWorkspaceState
{
    /// <summary>
    /// Gets or sets the working documents.
    /// </summary>
    public List<WordWorkingDocument> Documents { get; set; } = [];

    /// <summary>
    /// Gets or sets the name of the document a tool works on when it is not told which one.
    /// </summary>
    public string ActiveDocument { get; set; }

    /// <summary>
    /// Gets or sets stored files of this workspace that no working document refers to any more but that could not
    /// be deleted when they were replaced, so deleting the workspace still removes them.
    /// </summary>
    public List<string> OrphanedBlobs { get; set; } = [];

    /// <summary>
    /// Gets or sets the identifiers of the generated documents that hold the page pictures previews showed in a
    /// chat interaction, so clearing its history deletes them with the workspace.
    /// </summary>
    public List<string> PreviewDocumentIds { get; set; } = [];

    /// <summary>
    /// Finds a working document by name, ignoring case.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The document, or <see langword="null"/>.</returns>
    public WordWorkingDocument Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();

        return Documents.FirstOrDefault(document => string.Equals(document.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
