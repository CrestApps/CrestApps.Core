namespace CrestApps.Core.Infrastructure.Indexing.Models;

/// <summary>
/// Represents a search result from a data source knowledge base index.
/// </summary>
public sealed class DataSourceSearchResult
{
    /// <summary>
    /// Gets or sets the reference ID of the source document.
    /// </summary>
    public string ReferenceId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the data source the row belongs to. A citation link on an ingested
    /// figure needs it, because the figure endpoint is scoped to its data source.
    /// </summary>
    public string DataSourceId { get; set; }

    /// <summary>
    /// Gets or sets the title of the source document.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the text content of the matching chunk.
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// Gets or sets the chunk index within the document.
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// Gets or sets the reference type that identifies the kind of source
    /// (e.g., "Content" for Orchard Core content items, or the source index profile type).
    /// </summary>
    public string ReferenceType { get; set; }

    /// <summary>
    /// Gets or sets the similarity score.
    /// </summary>
    public float Score { get; set; }

    /// <summary>
    /// Gets or sets what kind of knowledge the row holds. A row written before typed knowledge existed has
    /// no value, and is read as text.
    /// </summary>
    public string ContentType { get; set; } = KnowledgeObjectTypes.Text;

    /// <summary>
    /// Gets or sets the canonical identifier of the document the row ultimately belongs to.
    /// </summary>
    public string RootId { get; set; }

    /// <summary>
    /// Gets or sets the canonical identifier of the object the row hangs directly off, so a hit can be
    /// widened to the article it came from.
    /// </summary>
    public string ParentId { get; set; }

    /// <summary>
    /// Gets or sets the page the row was read from.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Gets or sets the filter values stored with the row, where the provider keeps them.
    /// </summary>
    public IReadOnlyDictionary<string, object> Filters { get; set; }
}
