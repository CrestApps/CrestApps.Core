using CrestApps.Core.AI.Models;
using CrestApps.Core.Mvc.Web.Areas.DataSources.ViewModels;
using Microsoft.AspNetCore.DataProtection;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class AIDataSourceViewModelTests
{
    [Fact]
    public void ApplyTo_PreservesExistingElasticsearchPasswordWhenBlank()
    {
        var protector = new EphemeralDataProtectionProvider().CreateProtector(AIDataSourceProtectionConstants.SourceSecretPurpose);
        var dataSource = new AIDataSource
        {
            SourceType = AIDataSourceSourceTypes.Elasticsearch,
        };
        dataSource.Put(new ElasticsearchSourceMetadata
        {
            Password = protector.Protect("existing-secret"),
        });

        var model = new AIDataSourceViewModel
        {
            DisplayText = "External docs",
            SourceType = AIDataSourceSourceTypes.Elasticsearch,
            ElasticsearchEnvironmentType = ElasticsearchSourceMetadata.SelfManagedEnvironmentType,
            ElasticsearchUrl = "https://cluster",
            ElasticsearchAuthenticationType = ElasticsearchSourceMetadata.BasicAuthenticationType,
            ElasticsearchIndexName = "articles",
            ElasticsearchUsername = "elastic",
            ElasticsearchCertificateFingerprint = "AA:BB",
        };

        model.ApplyTo(dataSource, protector);

        Assert.True(dataSource.TryGet<ElasticsearchSourceMetadata>(out var metadata));
        Assert.Equal("https://cluster", metadata.Url);
        Assert.Equal(ElasticsearchSourceMetadata.BasicAuthenticationType, metadata.AuthenticationType);
        Assert.Equal("articles", metadata.IndexName);
        Assert.Equal("elastic", metadata.Username);
        Assert.Equal("AA:BB", metadata.CertificateFingerprint);
        Assert.Equal("existing-secret", protector.Unprotect(metadata.Password));
    }

    [Fact]
    public void ApplyTo_WritesElasticsearchKeyIdAndKeyAuthentication()
    {
        var protector = new EphemeralDataProtectionProvider().CreateProtector(AIDataSourceProtectionConstants.SourceSecretPurpose);
        var dataSource = new AIDataSource();
        var model = new AIDataSourceViewModel
        {
            SourceType = AIDataSourceSourceTypes.Elasticsearch,
            ElasticsearchEnvironmentType = ElasticsearchSourceMetadata.CloudHostedEnvironmentType,
            ElasticsearchCloudId = "deployment-name:dXMtZWFzdC0xJGFiYyRkZWY=",
            ElasticsearchAuthenticationType = ElasticsearchSourceMetadata.KeyIdAndKeyAuthenticationType,
            ElasticsearchIndexName = "articles",
            ElasticsearchApiKeyId = "key-id",
            ElasticsearchApiKey = "raw-api-key",
        };

        model.ApplyTo(dataSource, protector);

        Assert.True(dataSource.TryGet<ElasticsearchSourceMetadata>(out var metadata));
        Assert.Equal("deployment-name:dXMtZWFzdC0xJGFiYyRkZWY=", metadata.CloudId);
        Assert.Equal(ElasticsearchSourceMetadata.KeyIdAndKeyAuthenticationType, metadata.AuthenticationType);
        Assert.Equal("key-id", metadata.ApiKeyId);
        Assert.Equal("raw-api-key", protector.Unprotect(metadata.ApiKey));
        Assert.Null(metadata.Base64ApiKey);
    }

    [Fact]
    public void ApplyTo_ClearsElasticsearchCloudIdForSelfManagedEnvironment()
    {
        var protector = new EphemeralDataProtectionProvider().CreateProtector(AIDataSourceProtectionConstants.SourceSecretPurpose);
        var dataSource = new AIDataSource();
        var model = new AIDataSourceViewModel
        {
            SourceType = AIDataSourceSourceTypes.Elasticsearch,
            ElasticsearchEnvironmentType = ElasticsearchSourceMetadata.SelfManagedEnvironmentType,
            ElasticsearchUrl = "https://cluster",
            ElasticsearchCloudId = "deployment-name:dXMtZWFzdC0xJGFiYyRkZWY=",
            ElasticsearchIndexName = "articles",
        };

        model.ApplyTo(dataSource, protector);

        Assert.True(dataSource.TryGet<ElasticsearchSourceMetadata>(out var metadata));
        Assert.Equal(ElasticsearchSourceMetadata.SelfManagedEnvironmentType, metadata.EnvironmentType);
        Assert.Equal("https://cluster", metadata.Url);
        Assert.Null(metadata.CloudId);
    }

    [Fact]
    public void ApplyTo_ProtectsAzureAISearchApiKey()
    {
        var protector = new EphemeralDataProtectionProvider().CreateProtector(AIDataSourceProtectionConstants.SourceSecretPurpose);
        var dataSource = new AIDataSource();
        var model = new AIDataSourceViewModel
        {
            SourceType = AIDataSourceSourceTypes.AzureAISearch,
            AzureAISearchEndpoint = "https://kb.search.windows.net",
            AzureAISearchAuthenticationType = AzureAISearchSourceMetadata.ApiKeyAuthenticationType,
            AzureAISearchIndexName = "kb",
            AzureAISearchApiKey = "secret-key",
        };

        model.ApplyTo(dataSource, protector);

        Assert.True(dataSource.TryGet<AzureAISearchSourceMetadata>(out var metadata));
        Assert.Equal("https://kb.search.windows.net", metadata.Endpoint);
        Assert.Equal(AzureAISearchSourceMetadata.ApiKeyAuthenticationType, metadata.AuthenticationType);
        Assert.Equal("kb", metadata.IndexName);
        Assert.NotEqual("secret-key", metadata.ApiKey);
        Assert.Equal("secret-key", protector.Unprotect(metadata.ApiKey));
    }

    [Fact]
    public void ApplyTo_WritesAzureAISearchManagedIdentitySettings()
    {
        var protector = new EphemeralDataProtectionProvider().CreateProtector(AIDataSourceProtectionConstants.SourceSecretPurpose);
        var dataSource = new AIDataSource();
        var model = new AIDataSourceViewModel
        {
            SourceType = AIDataSourceSourceTypes.AzureAISearch,
            AzureAISearchEndpoint = "https://kb.search.windows.net",
            AzureAISearchAuthenticationType = AzureAISearchSourceMetadata.ManagedIdentityAuthenticationType,
            AzureAISearchIndexName = "kb",
            AzureAISearchIdentityClientId = "client-id",
            AzureAISearchApiKey = "ignored-secret",
        };

        model.ApplyTo(dataSource, protector);

        Assert.True(dataSource.TryGet<AzureAISearchSourceMetadata>(out var metadata));
        Assert.Equal(AzureAISearchSourceMetadata.ManagedIdentityAuthenticationType, metadata.AuthenticationType);
        Assert.Equal("client-id", metadata.IdentityClientId);
        Assert.Null(metadata.ApiKey);
    }

    [Fact]
    public void FromDataSource_ReadsConfiguredPostgreSQLSource()
    {
        var dataSource = new AIDataSource
        {
            ItemId = "ds-1",
            DisplayText = "PostgreSQL docs",
            SourceType = AIDataSourceSourceTypes.PostgreSQL,
            AIKnowledgeBaseIndexProfileName = "kb-index",
            KeyFieldName = "id",
            TitleFieldName = "title",
            ContentFieldName = "content",
        };
        dataSource.Put(new PostgreSQLSourceMetadata
        {
            TableName = "public.articles",
        });

        var model = AIDataSourceViewModel.FromDataSource(dataSource);

        Assert.Equal("ds-1", model.ItemId);
        Assert.Equal(AIDataSourceSourceTypes.PostgreSQL, model.SourceType);
        Assert.Equal("public.articles", model.PostgreSQLTableName);
        Assert.Equal("kb-index", model.AIKnowledgeBaseIndexProfileName);
    }
}
