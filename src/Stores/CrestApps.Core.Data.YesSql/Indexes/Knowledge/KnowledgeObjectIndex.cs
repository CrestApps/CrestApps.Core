using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Data.YesSql.Indexes.Knowledge;

/// <summary>
/// YesSql map index for <see cref="KnowledgeObject"/>, keyed by the owning data source (its source) and by
/// the identifiers retrieval and backfill look objects up by.
/// </summary>
public sealed class KnowledgeObjectIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the owning data source identifier (the object's source).
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets the canonical identifier.
    /// </summary>
    public string CanonicalId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the document the object belongs to.
    /// </summary>
    public string RootId { get; set; }

    /// <summary>
    /// Gets or sets what kind of knowledge the object holds.
    /// </summary>
    public string ObjectType { get; set; }

    /// <summary>
    /// Gets or sets how far along the object is.
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// Gets or sets the hash of the bytes, which the description cache looks up by.
    /// </summary>
    public string ContentHash { get; set; }
}
