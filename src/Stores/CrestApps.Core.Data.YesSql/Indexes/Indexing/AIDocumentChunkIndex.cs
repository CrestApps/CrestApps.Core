using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Data.YesSql.Indexes.Indexing;

/// <summary>
/// YesSql map index for <see cref="AIDocumentChunk"/>, storing the item identifier,
/// parent document, external reference, and positional index for efficient chunk queries.
/// </summary>
public sealed class AIDocumentChunkIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the identifier of the parent <see cref="AIDocument"/> that owns this chunk.
    /// </summary>
    public string AIDocumentId { get; set; }

    /// <summary>
    /// Gets or sets an external reference identifier associated with this chunk.
    /// </summary>
    public string ReferenceId { get; set; }

    /// <summary>
    /// Gets or sets the type qualifier for <see cref="ReferenceId"/>,
    /// distinguishing between different reference domains.
    /// </summary>
    public string ReferenceType { get; set; }

    /// <summary>
    /// Gets or sets the zero-based ordinal position of this chunk within its parent document.
    /// </summary>
    public int Index { get; set; }
}
