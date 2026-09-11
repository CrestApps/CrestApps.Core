using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Tooling.Instances.DataSources;

/// <summary>
/// The user-provided settings for a data source search tool instance. The settings are persisted in the
/// owning <see cref="AIToolInstance.Properties"/> and bind the produced function to a single
/// <see cref="AIDataSource"/> along with the retrieval parameters applied to every search it runs.
/// </summary>
public sealed class DataSourceSearchToolSettings
{
    /// <summary>
    /// Gets or sets the identifier of the AI data source this instance searches.
    /// </summary>
    public string DataSourceId { get; set; }

    /// <summary>
    /// Gets or sets the retrieval mode. <see cref="DataSourceRetrievalMode.Chunk"/> returns only the
    /// matching chunks; <see cref="DataSourceRetrievalMode.Hierarchical"/> returns the full source
    /// documents those chunks belong to.
    /// </summary>
    public DataSourceRetrievalMode RetrievalMode { get; set; } = DataSourceRetrievalMode.Chunk;

    /// <summary>
    /// Gets or sets the number of top-scoring documents to retrieve. When not set, the configured site
    /// default is used. Values outside the supported range fall back to that default.
    /// </summary>
    public int? TopNDocuments { get; set; }

    /// <summary>
    /// Gets or sets the strictness threshold used to decide how relevant a result must be to be returned.
    /// Values range from 1 to 5, with higher values applying a narrower filter.
    /// </summary>
    public int? Strictness { get; set; }

    /// <summary>
    /// Gets or sets the OData filter expression that narrows the search to a subset of the indexed data.
    /// It is translated to the index provider's own filter syntax before the search runs.
    /// </summary>
    public string Filter { get; set; }
}
