using CrestApps.Core.AI.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CrestApps.Core.Mvc.Web.Areas.DataSources.ViewModels;

public sealed class AIDataSourceViewModel
{
    public string ItemId { get; set; }

    public string DisplayText { get; set; }

    public string Source { get; set; } = AIDataSourceSourceTypes.SearchIndexProfile;

    public string SourceIndexProfileName { get; set; }

    public string AIKnowledgeBaseIndexProfileName { get; set; }

    public string KeyFieldName { get; set; }

    public string TitleFieldName { get; set; }

    public string ContentFieldName { get; set; }

    public string ElasticsearchUrl { get; set; }

    public string ElasticsearchCloudId { get; set; }

    public string ElasticsearchEnvironmentType { get; set; } = ElasticsearchSourceMetadata.SelfManagedEnvironmentType;

    public string ElasticsearchAuthenticationType { get; set; } = ElasticsearchSourceMetadata.NoneAuthenticationType;

    public string ElasticsearchIndexName { get; set; }

    public string ElasticsearchUsername { get; set; }

    public string ElasticsearchPassword { get; set; }

    public string ElasticsearchApiKey { get; set; }

    public string ElasticsearchBase64ApiKey { get; set; }

    public string ElasticsearchApiKeyId { get; set; }

    public string ElasticsearchCertificateFingerprint { get; set; }

    public string AzureAISearchEndpoint { get; set; }

    public string AzureAISearchAuthenticationType { get; set; } = AzureAISearchSourceMetadata.ApiKeyAuthenticationType;

    public string AzureAISearchIndexName { get; set; }

    public string AzureAISearchIdentityClientId { get; set; }

    public string AzureAISearchApiKey { get; set; }

    public string PostgreSQLConnectionString { get; set; }

    public string PostgreSQLTableName { get; set; }

    [BindNever]
    public IEnumerable<SelectListItem> SourceTypes { get; set; } = [];

    [BindNever]
    public IEnumerable<SelectListItem> SourceIndexProfiles { get; set; } = [];

    [BindNever]
    public IEnumerable<SelectListItem> KnowledgeBaseIndexProfiles { get; set; } = [];

    public static AIDataSourceViewModel FromDataSource(AIDataSource ds)
    {
        var model = new AIDataSourceViewModel
        {
            ItemId = ds.ItemId,
            DisplayText = ds.DisplayText,
            Source = string.IsNullOrWhiteSpace(ds.Source) ? AIDataSourceSourceTypes.SearchIndexProfile : ds.Source,
            SourceIndexProfileName = ds.SourceIndexProfileName,
            AIKnowledgeBaseIndexProfileName = ds.AIKnowledgeBaseIndexProfileName,
            KeyFieldName = ds.KeyFieldName,
            TitleFieldName = ds.TitleFieldName,
            ContentFieldName = ds.ContentFieldName,
        };

        if (ds.TryGet<ElasticsearchSourceMetadata>(out var elasticsearch))
        {
            model.ElasticsearchEnvironmentType = elasticsearch.GetEnvironmentType();
            model.ElasticsearchUrl = elasticsearch.Url;
            model.ElasticsearchCloudId = elasticsearch.CloudId;
            model.ElasticsearchAuthenticationType = elasticsearch.GetAuthenticationType();
            model.ElasticsearchIndexName = elasticsearch.IndexName;
            model.ElasticsearchUsername = elasticsearch.Username;
            model.ElasticsearchApiKeyId = elasticsearch.ApiKeyId;
            model.ElasticsearchCertificateFingerprint = elasticsearch.CertificateFingerprint;
        }

        if (ds.TryGet<AzureAISearchSourceMetadata>(out var azureAISearch))
        {
            model.AzureAISearchEndpoint = azureAISearch.Endpoint;
            model.AzureAISearchAuthenticationType = azureAISearch.GetAuthenticationType();
            model.AzureAISearchIndexName = azureAISearch.IndexName;
            model.AzureAISearchIdentityClientId = azureAISearch.IdentityClientId;
        }

        if (ds.TryGet<PostgreSQLSourceMetadata>(out var postgreSQL))
        {
            model.PostgreSQLTableName = postgreSQL.TableName;
        }

        return model;
    }

    public void ApplyTo(AIDataSource ds, IDataProtector protector)
    {
        ArgumentNullException.ThrowIfNull(ds);
        ArgumentNullException.ThrowIfNull(protector);

        ds.DisplayText = DisplayText?.Trim();
        ds.Source = string.IsNullOrWhiteSpace(Source) ? AIDataSourceSourceTypes.SearchIndexProfile : Source.Trim();
        ds.SourceIndexProfileName = SourceIndexProfileName;
        ds.AIKnowledgeBaseIndexProfileName = AIKnowledgeBaseIndexProfileName;
        ds.KeyFieldName = KeyFieldName?.Trim();
        ds.TitleFieldName = TitleFieldName?.Trim();
        ds.ContentFieldName = ContentFieldName?.Trim();

        ds.TryGet<ElasticsearchSourceMetadata>(out var existingElasticsearchMetadata);
        ds.TryGet<AzureAISearchSourceMetadata>(out var existingAzureAISearchMetadata);
        ds.TryGet<PostgreSQLSourceMetadata>(out var existingPostgreSQLMetadata);

        ds.Remove<ElasticsearchSourceMetadata>();
        ds.Remove<AzureAISearchSourceMetadata>();
        ds.Remove<PostgreSQLSourceMetadata>();

        if (string.Equals(ds.Source, AIDataSourceSourceTypes.Elasticsearch, StringComparison.OrdinalIgnoreCase))
        {
            var environmentType = NormalizeElasticsearchEnvironmentType(ElasticsearchEnvironmentType);
            var authenticationType = NormalizeElasticsearchAuthenticationType(ElasticsearchAuthenticationType);

            ds.Put(new ElasticsearchSourceMetadata
            {
                EnvironmentType = environmentType,
                Url = environmentType == ElasticsearchSourceMetadata.SelfManagedEnvironmentType ? ElasticsearchUrl?.Trim() : null,
                CloudId = environmentType == ElasticsearchSourceMetadata.CloudHostedEnvironmentType ? ElasticsearchCloudId?.Trim() : null,
                AuthenticationType = authenticationType,
                IndexName = ElasticsearchIndexName?.Trim(),
                Username = authenticationType == ElasticsearchSourceMetadata.BasicAuthenticationType ? ElasticsearchUsername?.Trim() : null,
                Password = authenticationType == ElasticsearchSourceMetadata.BasicAuthenticationType
                    ? ProtectSecret(ElasticsearchPassword, existingElasticsearchMetadata?.Password, protector)
                    : null,
                ApiKey = authenticationType == ElasticsearchSourceMetadata.ApiKeyAuthenticationType ||
                    authenticationType == ElasticsearchSourceMetadata.KeyIdAndKeyAuthenticationType
                    ? ProtectSecret(ElasticsearchApiKey, existingElasticsearchMetadata?.ApiKey, protector)
                    : null,
                Base64ApiKey = authenticationType == ElasticsearchSourceMetadata.Base64ApiKeyAuthenticationType
                    ? ProtectSecret(ElasticsearchBase64ApiKey, existingElasticsearchMetadata?.Base64ApiKey, protector)
                    : null,
                ApiKeyId = authenticationType == ElasticsearchSourceMetadata.KeyIdAndKeyAuthenticationType ? ElasticsearchApiKeyId?.Trim() : null,
                CertificateFingerprint = ElasticsearchCertificateFingerprint?.Trim(),
            });
        }

        if (string.Equals(ds.Source, AIDataSourceSourceTypes.AzureAISearch, StringComparison.OrdinalIgnoreCase))
        {
            var authenticationType = string.Equals(AzureAISearchAuthenticationType, AzureAISearchSourceMetadata.ManagedIdentityAuthenticationType, StringComparison.OrdinalIgnoreCase)
                ? AzureAISearchSourceMetadata.ManagedIdentityAuthenticationType
                : string.Equals(AzureAISearchAuthenticationType, AzureAISearchSourceMetadata.DefaultAuthenticationType, StringComparison.OrdinalIgnoreCase)
                    ? AzureAISearchSourceMetadata.DefaultAuthenticationType
                    : AzureAISearchSourceMetadata.ApiKeyAuthenticationType;
            ds.Put(new AzureAISearchSourceMetadata
            {
                Endpoint = AzureAISearchEndpoint?.Trim(),
                AuthenticationType = authenticationType,
                IndexName = AzureAISearchIndexName?.Trim(),
                IdentityClientId = string.Equals(authenticationType, AzureAISearchSourceMetadata.ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : AzureAISearchIdentityClientId?.Trim(),
                ApiKey = string.Equals(authenticationType, AzureAISearchSourceMetadata.ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase)
                    ? string.IsNullOrWhiteSpace(AzureAISearchApiKey)
                        ? existingAzureAISearchMetadata?.ApiKey
                        : protector.Protect(AzureAISearchApiKey)
                    : null,
            });
        }

        if (string.Equals(ds.Source, AIDataSourceSourceTypes.PostgreSQL, StringComparison.OrdinalIgnoreCase))
        {
            ds.Put(new PostgreSQLSourceMetadata
            {
                TableName = PostgreSQLTableName?.Trim(),
                ConnectionString = string.IsNullOrWhiteSpace(PostgreSQLConnectionString)
                    ? existingPostgreSQLMetadata?.ConnectionString
                    : protector.Protect(PostgreSQLConnectionString),
            });
        }
    }

    private static string NormalizeElasticsearchEnvironmentType(string environmentType)
    {
        if (string.Equals(environmentType, ElasticsearchSourceMetadata.CloudHostedEnvironmentType, StringComparison.OrdinalIgnoreCase))
        {
            return ElasticsearchSourceMetadata.CloudHostedEnvironmentType;
        }

        return ElasticsearchSourceMetadata.SelfManagedEnvironmentType;
    }

    private static string NormalizeElasticsearchAuthenticationType(string authenticationType)
    {
        if (string.Equals(authenticationType, ElasticsearchSourceMetadata.BasicAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            return ElasticsearchSourceMetadata.BasicAuthenticationType;
        }

        if (string.Equals(authenticationType, ElasticsearchSourceMetadata.ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            return ElasticsearchSourceMetadata.ApiKeyAuthenticationType;
        }

        if (string.Equals(authenticationType, ElasticsearchSourceMetadata.Base64ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            return ElasticsearchSourceMetadata.Base64ApiKeyAuthenticationType;
        }

        if (string.Equals(authenticationType, ElasticsearchSourceMetadata.KeyIdAndKeyAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            return ElasticsearchSourceMetadata.KeyIdAndKeyAuthenticationType;
        }

        return ElasticsearchSourceMetadata.NoneAuthenticationType;
    }

    private static string ProtectSecret(string value, string existingValue, IDataProtector protector)
    {
        return string.IsNullOrWhiteSpace(value)
            ? existingValue
            : protector.Protect(value);
    }
}
