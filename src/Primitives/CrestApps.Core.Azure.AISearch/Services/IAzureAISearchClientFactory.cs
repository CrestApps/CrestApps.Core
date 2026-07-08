using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;

namespace CrestApps.Core.Azure.AISearch.Services;

/// <summary>
/// Creates Azure AI Search clients from the configured connection options.
/// </summary>
public interface IAzureAISearchClientFactory
{
    /// <summary>
    /// Creates the configured Azure AI Search index client.
    /// </summary>
    /// <returns>The configured Azure AI Search index client.</returns>
    SearchIndexClient CreateSearchIndexClient();

    /// <summary>
    /// Creates the configured Azure AI Search client for a specific index.
    /// </summary>
    /// <param name="indexFullName">The remote index name.</param>
    /// <returns>The Azure AI Search client for the specified index.</returns>
    SearchClient CreateSearchClient(string indexFullName);

    /// <summary>
    /// Creates an Azure AI Search client for a specific index using explicit connection options.
    /// </summary>
    /// <param name="indexFullName">The remote index name.</param>
    /// <param name="configuration">The explicit connection options.</param>
    /// <returns>The Azure AI Search client for the specified index.</returns>
    SearchClient CreateSearchClient(string indexFullName, AzureAISearchConnectionOptions configuration);
}
