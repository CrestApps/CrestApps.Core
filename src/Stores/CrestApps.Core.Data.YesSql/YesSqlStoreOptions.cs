namespace CrestApps.Core.Data.YesSql;

/// <summary>
/// Configures the YesSql collection names used by each store, index provider,
/// and schema migration. Consumers can override any property to control which
/// YesSql collection a given category of data is stored in.
/// </summary>
public sealed class YesSqlStoreOptions
{
    /// <summary>
    /// Collection name used for non-AI data (e.g., search-index profiles).
    /// Defaults to <see langword="null"/> (the YesSql default collection).
    /// </summary>
    public string DefaultCollectionName { get; set; }

    /// <summary>
    /// Collection name used for general AI data such as profiles, deployments,
    /// connections, chat sessions, and chat interactions.
    /// </summary>
    public string AICollectionName { get; set; } = "AI";

    /// <summary>
    /// Collection name used for AI memory entries.
    /// </summary>
    public string AIMemoryCollectionName { get; set; } = "AIMemory";

    /// <summary>
    /// Collection name used for AI document processing data
    /// (documents, document chunks, and data sources).
    /// </summary>
    public string AIDocsCollectionName { get; set; } = "AIDocs";
}
