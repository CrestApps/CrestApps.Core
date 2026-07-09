using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Elasticsearch.Services;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Support;
using Elastic.Clients.Elasticsearch;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Elasticsearch.Services;

internal sealed class ElasticsearchAIDataSourceSourceHandler : IAIDataSourceSourceHandler
{
    private readonly IElasticsearchClientFactory _clientFactory;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger<ElasticsearchAIDataSourceSourceHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElasticsearchAIDataSourceSourceHandler"/> class.
    /// </summary>
    /// <param name="clientFactory">The client factory.</param>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    /// <param name="logger">The logger.</param>
    public ElasticsearchAIDataSourceSourceHandler(
        IElasticsearchClientFactory clientFactory,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<ElasticsearchAIDataSourceSourceHandler> logger)
    {
        _clientFactory = clientFactory;
        _dataProtectionProvider = dataProtectionProvider;
        _logger = logger;
    }

    /// <summary>
    /// Gets the source type.
    /// </summary>
    public string SourceType => AIDataSourceSourceTypes.Elasticsearch;

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

        if (!dataSource.TryGet<ElasticsearchSourceMetadata>(out var metadata))
        {
            result.Fail(new ValidationResult("Elasticsearch source settings are required.", [nameof(ElasticsearchSourceMetadata)]));

            return ValueTask.CompletedTask;
        }

        var environmentType = metadata.GetEnvironmentType();

        if (string.Equals(environmentType, ElasticsearchSourceMetadata.CloudHostedEnvironmentType, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(metadata.CloudId))
            {
                result.Fail(new ValidationResult("Elastic Cloud ID is required for cloud-hosted Elasticsearch deployments.", [nameof(ElasticsearchSourceMetadata.EnvironmentType), nameof(ElasticsearchSourceMetadata.CloudId)]));
            }
        }
        else if (string.IsNullOrWhiteSpace(metadata.Url))
        {
            result.Fail(new ValidationResult("Elasticsearch URL is required for self-managed deployments.", [nameof(ElasticsearchSourceMetadata.EnvironmentType), nameof(ElasticsearchSourceMetadata.Url)]));
        }

        if (string.IsNullOrWhiteSpace(metadata.IndexName))
        {
            result.Fail(new ValidationResult("Elasticsearch index name is required.", [nameof(ElasticsearchSourceMetadata.IndexName)]));
        }

        var authenticationType = metadata.GetAuthenticationType();
        var hasUsername = !string.IsNullOrWhiteSpace(metadata.Username);
        var hasPassword = !string.IsNullOrWhiteSpace(metadata.Password);

        if (string.Equals(authenticationType, ElasticsearchSourceMetadata.BasicAuthenticationType, StringComparison.OrdinalIgnoreCase) &&
            hasUsername != hasPassword)
        {
            result.Fail(new ValidationResult("Elasticsearch basic authentication requires both username and password.", [nameof(ElasticsearchSourceMetadata.Username), nameof(ElasticsearchSourceMetadata.Password)]));
        }

        if (string.Equals(authenticationType, ElasticsearchSourceMetadata.ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(metadata.ApiKey))
        {
            result.Fail(new ValidationResult("Elasticsearch API key authentication requires an API key.", [nameof(ElasticsearchSourceMetadata.ApiKey)]));
        }

        if (string.Equals(authenticationType, ElasticsearchSourceMetadata.Base64ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(metadata.Base64ApiKey))
        {
            result.Fail(new ValidationResult("Elasticsearch base64 API key authentication requires a base64-encoded API key.", [nameof(ElasticsearchSourceMetadata.Base64ApiKey)]));
        }

        if (string.Equals(authenticationType, ElasticsearchSourceMetadata.KeyIdAndKeyAuthenticationType, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(metadata.ApiKeyId) || string.IsNullOrWhiteSpace(metadata.ApiKey)))
        {
            result.Fail(new ValidationResult("Elasticsearch key ID and key authentication requires both an API key ID and API key.", [nameof(ElasticsearchSourceMetadata.ApiKeyId), nameof(ElasticsearchSourceMetadata.ApiKey)]));
        }

        if (string.Equals(environmentType, ElasticsearchSourceMetadata.CloudHostedEnvironmentType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(authenticationType, ElasticsearchSourceMetadata.NoneAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            result.Fail(new ValidationResult("Elastic Cloud connections require an authentication type and matching credentials.", [nameof(ElasticsearchSourceMetadata.EnvironmentType), nameof(ElasticsearchSourceMetadata.AuthenticationType), nameof(ElasticsearchSourceMetadata.CloudId)]));
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
        var (client, metadata) = Resolve(dataSource);

        var searchAfterValue = default(string);
        while (!cancellationToken.IsCancellationRequested)
        {
            var response = searchAfterValue == null
                ? await client.SearchAsync<System.Text.Json.Nodes.JsonObject>(search => search
                    .Indices(metadata.IndexName)
                    .Size(1000)
                    .Sort(sort => sort.Field("_doc")), cancellationToken)
                : await client.SearchAsync<System.Text.Json.Nodes.JsonObject>(search => search
                    .Indices(metadata.IndexName)
                    .Size(1000)
                    .Sort(sort => sort.Field("_doc"))
                    .SearchAfter([searchAfterValue]), cancellationToken);

            if (!response.IsValidResponse || response.Hits.Count == 0)
            {
                yield break;
            }

            foreach (var hit in response.Hits)
            {
                if (hit.Source == null)
                {
                    continue;
                }

                yield return CreateDocumentPair(dataSource, hit.Id, hit.Source);
            }

            var lastSort = response.Hits.Last().Sort;
            searchAfterValue = lastSort != null && lastSort.Count > 0
                ? lastSort.First().ToString()
                : null;

            if (string.IsNullOrEmpty(searchAfterValue) || response.Hits.Count < 1000)
            {
                yield break;
            }
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

        var (client, metadata) = Resolve(dataSource);
        var response = await client.SearchAsync<System.Text.Json.Nodes.JsonObject>(search => search
            .Indices(metadata.IndexName)
            .Query(query => query.Ids(idsQuery => idsQuery.Values(new Ids(ids))))
            .Size(ids.Length), cancellationToken);

        if (!response.IsValidResponse || response.Hits == null)
        {
            yield break;
        }

        foreach (var hit in response.Hits)
        {
            if (hit.Source == null)
            {
                continue;
            }

            yield return CreateDocumentPair(dataSource, hit.Id, hit.Source);
        }
    }

    private static KeyValuePair<string, SourceDocument> CreateDocumentPair(AIDataSource dataSource, string nativeId, System.Text.Json.Nodes.JsonObject source)
    {
        var key = nativeId;
        if (!string.IsNullOrWhiteSpace(dataSource.KeyFieldName))
        {
            var keyNode = ResolveFieldValue(source, dataSource.KeyFieldName);
            if (keyNode != null)
            {
                key = keyNode.GetStringValue() ?? key;
            }
        }

        return new KeyValuePair<string, SourceDocument>(key, ExtractDocument(source, dataSource.TitleFieldName, dataSource.ContentFieldName));
    }

    private (ElasticsearchClient Client, ElasticsearchSourceMetadata Metadata) Resolve(AIDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        if (!dataSource.TryGet<ElasticsearchSourceMetadata>(out var metadata))
        {
            throw new InvalidOperationException("Elasticsearch source metadata is missing.");
        }

        var environmentType = metadata.GetEnvironmentType();
        var authenticationType = metadata.GetAuthenticationType();
        var protector = _dataProtectionProvider.CreateProtector(AIDataSourceProtectionConstants.SourceSecretPurpose);
        var password = string.Equals(authenticationType, ElasticsearchSourceMetadata.BasicAuthenticationType, StringComparison.OrdinalIgnoreCase)
            ? DataProtectionHelper.Unprotect(protector, metadata.Password, _logger, "Failed to unprotect AI data source field '{FieldName}' for data source '{DataSourceId}'.", nameof(ElasticsearchSourceMetadata.Password), dataSource.ItemId)
            : null;
        var apiKey = string.Equals(authenticationType, ElasticsearchSourceMetadata.ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authenticationType, ElasticsearchSourceMetadata.KeyIdAndKeyAuthenticationType, StringComparison.OrdinalIgnoreCase)
            ? DataProtectionHelper.Unprotect(protector, metadata.ApiKey, _logger, "Failed to unprotect AI data source field '{FieldName}' for data source '{DataSourceId}'.", nameof(ElasticsearchSourceMetadata.ApiKey), dataSource.ItemId)
            : null;
        var base64ApiKey = string.Equals(authenticationType, ElasticsearchSourceMetadata.Base64ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase)
            ? DataProtectionHelper.Unprotect(protector, metadata.Base64ApiKey, _logger, "Failed to unprotect AI data source field '{FieldName}' for data source '{DataSourceId}'.", nameof(ElasticsearchSourceMetadata.Base64ApiKey), dataSource.ItemId)
            : null;

        var client = _clientFactory.Create(new ElasticsearchConnectionOptions
        {
            Url = string.Equals(environmentType, ElasticsearchSourceMetadata.CloudHostedEnvironmentType, StringComparison.OrdinalIgnoreCase)
                ? null
                : metadata.Url,
            CloudId = string.Equals(environmentType, ElasticsearchSourceMetadata.CloudHostedEnvironmentType, StringComparison.OrdinalIgnoreCase)
                ? metadata.CloudId
                : null,
            AuthenticationType = authenticationType,
            Username = string.Equals(authenticationType, ElasticsearchSourceMetadata.BasicAuthenticationType, StringComparison.OrdinalIgnoreCase) ? metadata.Username : null,
            Password = password,
            ApiKeyId = string.Equals(authenticationType, ElasticsearchSourceMetadata.KeyIdAndKeyAuthenticationType, StringComparison.OrdinalIgnoreCase) ? metadata.ApiKeyId : null,
            ApiKey = apiKey,
            Base64ApiKey = base64ApiKey,
            CertificateFingerprint = metadata.CertificateFingerprint,
        });

        return (client, metadata);
    }

    private static SourceDocument ExtractDocument(System.Text.Json.Nodes.JsonObject source, string titleFieldName, string contentFieldName)
    {
        string title = null;
        string content = null;

        if (!string.IsNullOrWhiteSpace(titleFieldName))
        {
            title = ResolveFieldValue(source, titleFieldName).GetStringValue();
        }

        if (!string.IsNullOrWhiteSpace(contentFieldName))
        {
            content = ResolveFieldValue(source, contentFieldName).GetStringValue();
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            content = source.ToJsonString();
        }

        if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(content))
        {
            title = content.ExtractTitleFromContent();
        }

        var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in source)
        {
            fields[property.Key] = property.Value.GetRawValue();
        }

        return new SourceDocument
        {
            Title = title,
            Content = content,
            Fields = fields,
        };
    }

    private static System.Text.Json.Nodes.JsonNode ResolveFieldValue(System.Text.Json.Nodes.JsonObject source, string fieldPath)
    {
        if (source == null || string.IsNullOrWhiteSpace(fieldPath))
        {
            return null;
        }

        if (source.TryGetPropertyValue(fieldPath, out var directNode))
        {
            return directNode;
        }

        if (!fieldPath.Contains('.'))
        {
            return null;
        }

        System.Text.Json.Nodes.JsonNode current = source;
        foreach (var segment in fieldPath.Split('.'))
        {
            if (current is not System.Text.Json.Nodes.JsonObject obj || !obj.TryGetPropertyValue(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }
}
