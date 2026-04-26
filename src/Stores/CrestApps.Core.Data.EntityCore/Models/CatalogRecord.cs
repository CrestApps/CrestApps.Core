namespace CrestApps.Core.Data.EntityCore.Models;

/// <summary>
/// Represents the Entity Framework Core database record used by the catalog store
/// to persist any catalogued entity type alongside its searchable index columns.
/// </summary>
public sealed class CatalogRecord
{
    /// <summary>
    /// Gets or sets the CLR or logical type name of the catalogued entity,
    /// used as part of the composite primary key.
    /// </summary>
    public string EntityType { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the catalogued item,
    /// used as part of the composite primary key.
    /// </summary>
    public string ItemId { get; set; }

    /// <summary>
    /// Gets or sets the unique technical name of the item, used for name-based lookups.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the human-readable display text of the item.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the source or provider name associated with the item.
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets the chat session identifier linked to the item, if applicable.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the chat interaction identifier linked to the item, if applicable.
    /// </summary>
    public string ChatInteractionId { get; set; }

    /// <summary>
    /// Gets or sets an external reference identifier associated with the item.
    /// </summary>
    public string ReferenceId { get; set; }

    /// <summary>
    /// Gets or sets the type qualifier for <see cref="ReferenceId"/>,
    /// distinguishing between different reference domains.
    /// </summary>
    public string ReferenceType { get; set; }

    /// <summary>
    /// Gets or sets the AI document identifier linked to the item, if applicable.
    /// </summary>
    public string AIDocumentId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who owns or is associated with the item.
    /// </summary>
    public string UserId { get; set; }

    /// <summary>
    /// Gets or sets an arbitrary type discriminator for the item within its entity type.
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the record was created, if tracked.
    /// </summary>
    public DateTime? CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the record was last updated, if tracked.
    /// </summary>
    public DateTime? UpdatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the serialized JSON payload containing the full entity data.
    /// </summary>
    public string Payload { get; set; }
}
