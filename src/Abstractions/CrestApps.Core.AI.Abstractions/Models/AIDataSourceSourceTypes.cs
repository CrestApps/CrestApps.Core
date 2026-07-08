namespace CrestApps.Core.AI.Models;

/// <summary>
/// Well-known AI data source source type identifiers.
/// </summary>
public static class AIDataSourceSourceTypes
{
    /// <summary>
    /// Source documents are read from a CrestApps search index profile.
    /// </summary>
    public const string SearchIndexProfile = "SearchIndexProfile";

    /// <summary>
    /// Source documents are read from an Elasticsearch index using explicit connection settings.
    /// </summary>
    public const string Elasticsearch = "Elasticsearch";

    /// <summary>
    /// Source documents are read from an Azure AI Search index using explicit connection settings.
    /// </summary>
    public const string AzureAISearch = "AzureAISearch";

    /// <summary>
    /// Source documents are read from a PostgreSQL table using explicit connection settings.
    /// </summary>
    public const string PostgreSQL = "PostgreSQL";
}
