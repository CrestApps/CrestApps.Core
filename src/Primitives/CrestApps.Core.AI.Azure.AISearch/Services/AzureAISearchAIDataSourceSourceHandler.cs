using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using Azure.Search.Documents.Models;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Azure.AISearch;
using CrestApps.Core.Azure.AISearch.Services;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Azure.AISearch.Services;

internal sealed class AzureAISearchAIDataSourceSourceHandler : IAIDataSourceSourceHandler
{
    private readonly IAzureAISearchClientFactory _clientFactory;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger<AzureAISearchAIDataSourceSourceHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureAISearchAIDataSourceSourceHandler"/> class.
    /// </summary>
    /// <param name="clientFactory">The client factory.</param>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    /// <param name="logger">The logger.</param>
    public AzureAISearchAIDataSourceSourceHandler(
        IAzureAISearchClientFactory clientFactory,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<AzureAISearchAIDataSourceSourceHandler> logger)
    {
        _clientFactory = clientFactory;
        _dataProtectionProvider = dataProtectionProvider;
        _logger = logger;
    }

    /// <summary>
    /// Gets the source type.
    /// </summary>
    public string SourceType => AIDataSourceSourceTypes.AzureAISearch;

    /// <summary>
    /// Validates the operation.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    /// <param name="result">The result.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask ValidateAsync(AIDataSource dataSource, ValidationResultDetails result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(result);

        if (!dataSource.TryGet<AzureAISearchSourceMetadata>(out var metadata))
        {
            result.Fail(new ValidationResult("Azure AI Search source settings are required.", [nameof(AzureAISearchSourceMetadata)]));

            return ValueTask.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(metadata.Endpoint))
        {
            result.Fail(new ValidationResult("Azure AI Search endpoint is required.", [nameof(AzureAISearchSourceMetadata.Endpoint)]));
        }

        if (string.IsNullOrWhiteSpace(metadata.IndexName))
        {
            result.Fail(new ValidationResult("Azure AI Search index name is required.", [nameof(AzureAISearchSourceMetadata.IndexName)]));
        }

        if (string.Equals(metadata.GetAuthenticationType(), AzureAISearchSourceMetadata.ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(metadata.ApiKey))
        {
            result.Fail(new ValidationResult("Azure AI Search API key is required.", [nameof(AzureAISearchSourceMetadata.ApiKey)]));
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Gets reference type.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<string> GetReferenceTypeAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(SourceType);
    }

    /// <summary>
    /// Reads the operation.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadAsync(AIDataSource dataSource, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (searchClient, _) = Resolve(dataSource);
        var options = new global::Azure.Search.Documents.SearchOptions
        {
            Size = 1000,
            Select = { "*" },
        };

        var response = await searchClient.SearchAsync<SearchDocument>("*", options, cancellationToken);
        await foreach (var result in response.Value.GetResultsAsync())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            yield return CreateDocumentPair(dataSource, result.Document);
        }
    }

    /// <summary>
    /// Reads by ids.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    /// <param name="documentIds">The document ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadByIdsAsync(AIDataSource dataSource, IEnumerable<string> documentIds, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var ids = documentIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (ids.Length == 0)
        {
            yield break;
        }

        var (searchClient, _) = Resolve(dataSource);

        if (!string.IsNullOrWhiteSpace(dataSource.KeyFieldName))
        {
            var filter = DataSourceAzureAISearchDocumentIdFilterBuilder.BuildFilter(ids, dataSource.KeyFieldName);
            var options = new global::Azure.Search.Documents.SearchOptions
            {
                Filter = filter,
                Size = ids.Length,
                Select = { "*" },
            };

            var response = await searchClient.SearchAsync<SearchDocument>(null, options, cancellationToken);
            await foreach (var result in response.Value.GetResultsAsync())
            {
                yield return CreateDocumentPair(dataSource, result.Document);
            }

            yield break;
        }

        foreach (var id in ids)
        {
            SearchDocument document;

            try
            {
                var response = await searchClient.GetDocumentAsync<SearchDocument>(id, cancellationToken: cancellationToken);
                document = response?.Value;
            }
            catch
            {
                continue;
            }

            if (document != null)
            {
                yield return CreateDocumentPair(dataSource, document);
            }
        }
    }

    private static KeyValuePair<string, SourceDocument> CreateDocumentPair(AIDataSource dataSource, SearchDocument document)
    {
        var key = document.Keys.FirstOrDefault() is { } firstKey && document.TryGetValue(firstKey, out var firstKeyValue)
            ? firstKeyValue?.ToString()
            : null;

        if (!string.IsNullOrWhiteSpace(dataSource.KeyFieldName) && document.TryGetValue(dataSource.KeyFieldName, out var configuredKeyValue))
        {
            key = configuredKeyValue?.ToString() ?? key;
        }

        return new KeyValuePair<string, SourceDocument>(key, ExtractDocument(document, dataSource.TitleFieldName, dataSource.ContentFieldName));
    }

    private (global::Azure.Search.Documents.SearchClient SearchClient, AzureAISearchSourceMetadata Metadata) Resolve(AIDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        if (!dataSource.TryGet<AzureAISearchSourceMetadata>(out var metadata))
        {
            throw new InvalidOperationException("Azure AI Search source metadata is missing.");
        }

        var authenticationType = metadata.GetAuthenticationType();
        var protector = _dataProtectionProvider.CreateProtector(AIDataSourceProtectionConstants.SourceSecretPurpose);
        var apiKey = string.Equals(authenticationType, AzureAISearchSourceMetadata.ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase)
            ? DataProtectionHelper.Unprotect(protector, metadata.ApiKey, _logger, "Failed to unprotect AI data source field '{FieldName}' for data source '{DataSourceId}'.", nameof(AzureAISearchSourceMetadata.ApiKey), dataSource.ItemId)
            : null;
        var client = _clientFactory.CreateSearchClient(metadata.IndexName, new AzureAISearchConnectionOptions
        {
            Endpoint = metadata.Endpoint,
            AuthenticationType = authenticationType,
            ApiKey = apiKey,
            IdentityClientId = metadata.IdentityClientId,
        });

        return (client, metadata);
    }

    private static SourceDocument ExtractDocument(SearchDocument document, string titleFieldName, string contentFieldName)
    {
        string title = null;
        string content = null;

        if (!string.IsNullOrWhiteSpace(titleFieldName) && document.TryGetValue(titleFieldName, out var titleValue))
        {
            title = titleValue?.ToString();
        }

        if (!string.IsNullOrWhiteSpace(contentFieldName) && document.TryGetValue(contentFieldName, out var contentValue))
        {
            content = contentValue?.ToString();
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            content = System.Text.Json.JsonSerializer.Serialize(document);
        }

        if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(content))
        {
            title = content.ExtractTitleFromContent();
        }

        var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in document)
        {
            fields[kvp.Key] = kvp.Value;
        }

        return new SourceDocument
        {
            Title = title,
            Content = content,
            Fields = fields,
        };
    }
}
