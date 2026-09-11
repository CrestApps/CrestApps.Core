namespace CrestApps.Core.AI.Tooling.Instances.DataSources;

/// <summary>
/// Well-known identifiers for the built-in AI data source search tool instance source.
/// </summary>
public static class DataSourceSearchToolConstants
{
    /// <summary>
    /// The registered source name of the data source search source. It is stored as the
    /// <see cref="CrestApps.Core.Models.SourceCatalogEntry.Source"/> of every
    /// <see cref="AIToolInstance"/> created from it.
    /// </summary>
    public const string SourceName = "data-source-search";

    /// <summary>
    /// The category applied to the data source search source so it is grouped with the other
    /// knowledge-base tools.
    /// </summary>
    public const string Category = "Knowledgebase";
}
