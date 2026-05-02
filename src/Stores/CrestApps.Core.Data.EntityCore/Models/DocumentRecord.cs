namespace CrestApps.Core.Data.EntityCore.Models;

/// <summary>
/// Represents a centralized document record that stores the serialized JSON
/// payload for any entity managed by the EntityCore stores.
/// </summary>
/// <remarks>
/// Individual index tables (such as <see cref="CatalogRecord"/> and
/// <see cref="AIChatSessionRecord"/>) reference their document through the
/// <c>DocumentId</c> foreign key, following a pattern similar to YesSql's
/// shared Document table.
/// </remarks>
public sealed class DocumentRecord
{
    /// <summary>
    /// Gets or sets the database-generated identity for this document.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the CLR or logical type name of the entity stored in this document.
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the serialized JSON content of the entity.
    /// </summary>
    public string Content { get; set; }
}
