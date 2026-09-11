namespace CrestApps.Core.AI.Models;

/// <summary>
/// Specifies how matching content is returned from an AI data source knowledge base.
/// </summary>
public enum DataSourceRetrievalMode
{
    /// <summary>
    /// Returns only the individual chunks that matched the query. This keeps the context small and is the
    /// default.
    /// </summary>
    Chunk = 0,

    /// <summary>
    /// Returns the full source documents the matching chunks belong to. This gives the model complete
    /// context at the cost of a much larger payload.
    /// </summary>
    Hierarchical = 1,
}
