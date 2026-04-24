using Elastic.Clients.Elasticsearch;

namespace CrestApps.Core.Elasticsearch.Services;

/// <summary>
/// Creates Elasticsearch clients from the configured connection options.
/// </summary>
public interface IElasticsearchClientFactory
{
    /// <summary>
    /// Creates the configured Elasticsearch client.
    /// </summary>
    /// <returns>The configured Elasticsearch client.</returns>
    ElasticsearchClient Create();

    /// <summary>
    /// Creates an Elasticsearch client for the supplied configuration.
    /// </summary>
    /// <param name="configuration">The Elasticsearch connection settings to use.</param>
    /// <returns>The configured Elasticsearch client.</returns>
    ElasticsearchClient Create(ElasticsearchConnectionOptions configuration);
}
