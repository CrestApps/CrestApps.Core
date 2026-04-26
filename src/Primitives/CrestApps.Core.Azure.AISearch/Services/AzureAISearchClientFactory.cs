using System.Collections.Concurrent;
using Azure;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Azure.AISearch.Services;

/// <summary>
/// Creates Azure AI Search clients from the current connection options.
/// </summary>
public sealed class AzureAISearchClientFactory : IAzureAISearchClientFactory
{
    private readonly AzureAISearchConnectionOptions _options;
    private readonly object _syncLock = new();
    private readonly ConcurrentDictionary<string, SearchClient> _clients = new(StringComparer.Ordinal);

    private SearchIndexClient _searchIndexClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureAISearchClientFactory"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public AzureAISearchClientFactory(IOptions<AzureAISearchConnectionOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Creates search index client.
    /// </summary>
    public SearchIndexClient CreateSearchIndexClient()
    {
        if (_searchIndexClient != null)
        {
            return _searchIndexClient;
        }

        lock (_syncLock)
        {
            _searchIndexClient ??= CreateSearchIndexClient(_options);
        }

        return _searchIndexClient;
    }

    /// <summary>
    /// Creates search client.
    /// </summary>
    /// <param name="indexFullName">The index full name.</param>
    public SearchClient CreateSearchClient(string indexFullName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexFullName);

        var normalizedIndexFullName = indexFullName.Trim();

        return _clients.GetOrAdd(normalizedIndexFullName, static (name, factory) =>
        {
            return factory.CreateSearchIndexClient().GetSearchClient(name);
        }, this);
    }

    private static SearchIndexClient CreateSearchIndexClient(AzureAISearchConnectionOptions configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(configuration.Endpoint))
        {
            throw new InvalidOperationException("Azure AI Search is not configured.");
        }

        if (!Uri.TryCreate(configuration.Endpoint.Trim(), UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("The Azure AI Search endpoint is invalid.");
        }

        return !string.IsNullOrWhiteSpace(configuration.ApiKey)
            ? new SearchIndexClient(endpoint, new AzureKeyCredential(configuration.ApiKey.Trim()))
            : new SearchIndexClient(endpoint, new DefaultAzureCredential());
    }
}
