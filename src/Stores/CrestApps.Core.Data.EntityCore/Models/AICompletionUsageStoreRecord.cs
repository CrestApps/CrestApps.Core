namespace CrestApps.Core.Data.EntityCore.Models;

/// <summary>
/// Represents the Entity Framework Core database record for an AI completion usage event.
/// </summary>
public sealed class AICompletionUsageStoreRecord
{
    /// <summary>
    /// Gets or sets the database-generated identity for this record.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the foreign key to the <see cref="DocumentRecord"/> that holds
    /// the serialized JSON payload for this usage record.
    /// </summary>
    public long DocumentId { get; set; }

    /// <summary>
    /// Gets or sets the navigation property to the associated <see cref="DocumentRecord"/>.
    /// </summary>
    public DocumentRecord Document { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the usage record was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the optional chat session identifier associated with the usage record.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the optional interaction identifier associated with the usage record.
    /// </summary>
    public string InteractionId { get; set; }
}
