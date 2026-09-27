using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// What the workspace records about one deck.
/// </summary>
internal sealed class PresentationDeckState
{
    /// <summary>
    /// Gets or sets the deck's identifier, such as <c>deck-2</c>.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the deck's name, which the tools accept in place of its identifier.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the file name the deck is exported as.
    /// </summary>
    public string FileName { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the uploaded document the deck was imported from, when it was.
    /// </summary>
    public string SourceDocumentId { get; set; }

    /// <summary>
    /// Gets or sets the name of the uploaded file the deck was imported from, when it was.
    /// </summary>
    public string SourceFileName { get; set; }

    /// <summary>
    /// Gets or sets how many times the deck has been changed.
    /// </summary>
    public int Revision { get; set; }

    /// <summary>
    /// Gets or sets the earlier versions kept for undo, oldest first.
    /// </summary>
    public List<PresentationDeckRevision> History { get; set; } = [];

    /// <summary>
    /// Gets or sets the house style recorded for the deck.
    /// </summary>
    public PresentationFormatting Formatting { get; set; } = new();

    /// <summary>
    /// Gets or sets the tables and charts tied to tabular queries.
    /// </summary>
    public List<PresentationDataLink> DataLinks { get; set; } = [];

    /// <summary>
    /// Gets or sets when the deck was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets when the deck was last changed.
    /// </summary>
    public DateTime ModifiedUtc { get; set; }

    /// <summary>
    /// Gets or sets a description of the last change.
    /// </summary>
    public string LastChange { get; set; }
}
