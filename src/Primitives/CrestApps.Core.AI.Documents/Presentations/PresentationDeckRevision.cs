namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// An earlier version of a deck, kept so a change can be undone or compared.
/// </summary>
internal sealed class PresentationDeckRevision
{
    /// <summary>
    /// Gets or sets the revision number the version was saved as.
    /// </summary>
    public int Revision { get; set; }

    /// <summary>
    /// Gets or sets the store-relative path of the saved version.
    /// </summary>
    public string Path { get; set; }

    /// <summary>
    /// Gets or sets what the change that followed this version did, for the undo message.
    /// </summary>
    public string Description { get; set; }
}
