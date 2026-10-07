namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// The workspace's record of its decks, stored beside them.
/// </summary>
internal sealed class PresentationWorkspaceState
{
    /// <summary>
    /// Gets or sets the format version of the record.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Gets or sets the identifier of the deck tools act on when none is named.
    /// </summary>
    public string ActiveDeckId { get; set; }

    /// <summary>
    /// Gets or sets the number the next deck's identifier is made from.
    /// </summary>
    public int NextDeckNumber { get; set; } = 1;

    /// <summary>
    /// Gets or sets the decks.
    /// </summary>
    public List<PresentationDeckState> Decks { get; set; } = [];

    /// <summary>
    /// Gets or sets the identifiers of uploads the reader removed from the workspace, which are not imported
    /// again.
    /// </summary>
    public List<string> DismissedDocumentIds { get; set; } = [];
}
