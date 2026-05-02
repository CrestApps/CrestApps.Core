namespace CrestApps.Core.Data.EntityCore.Models;

/// <summary>
/// Represents the Entity Framework Core database record for an extracted-data
/// snapshot captured from a chat session.
/// </summary>
public sealed class AIChatSessionExtractedDataStoreRecord
{
    /// <summary>
    /// Gets or sets the database-generated identity for this record.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the foreign key to the <see cref="DocumentRecord"/> that holds
    /// the serialized JSON payload for this extracted-data snapshot.
    /// </summary>
    public long DocumentId { get; set; }

    /// <summary>
    /// Gets or sets the navigation property to the associated <see cref="DocumentRecord"/>.
    /// </summary>
    public DocumentRecord Document { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the chat session.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the AI profile associated with the session.
    /// </summary>
    public string ProfileId { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the session started.
    /// </summary>
    public DateTime SessionStartedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the session ended.
    /// </summary>
    public DateTime? SessionEndedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the extracted-data snapshot was last updated.
    /// </summary>
    public DateTime UpdatedUtc { get; set; }
}
