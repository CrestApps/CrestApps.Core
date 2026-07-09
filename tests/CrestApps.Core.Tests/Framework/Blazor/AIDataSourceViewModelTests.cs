using CrestApps.Core.AI.Models;
using CrestApps.Core.Blazor.Web.ViewModels;
using Microsoft.AspNetCore.DataProtection;

namespace CrestApps.Core.Tests.Framework.Blazor;

public sealed class AIDataSourceViewModelTests
{
    [Fact]
    public void ApplyTo_PreservesExistingPostgreSQLConnectionStringWhenBlank()
    {
        var protector = new EphemeralDataProtectionProvider().CreateProtector(AIDataSourceProtectionConstants.SourceSecretPurpose);
        var dataSource = new AIDataSource
        {
            Source = AIDataSourceSourceTypes.PostgreSQL,
        };
        dataSource.Put(new PostgreSQLSourceMetadata
        {
            ConnectionString = protector.Protect("Host=db;Database=kb"),
        });

        var model = new AIDataSourceViewModel
        {
            Source = AIDataSourceSourceTypes.PostgreSQL,
            PostgreSQLTableName = "kb_articles",
        };

        model.ApplyTo(dataSource, protector);

        Assert.True(dataSource.TryGet<PostgreSQLSourceMetadata>(out var metadata));
        Assert.Equal("kb_articles", metadata.TableName);
        Assert.Equal("Host=db;Database=kb", protector.Unprotect(metadata.ConnectionString));
    }

    [Fact]
    public void FromDataSource_DefaultsMissingSourceTypeToSearchIndexProfile()
    {
        var dataSource = new AIDataSource
        {
            SourceIndexProfileName = "content-index",
        };

        var model = AIDataSourceViewModel.FromDataSource(dataSource);

        Assert.Equal(AIDataSourceSourceTypes.SearchIndexProfile, model.Source);
        Assert.Equal("content-index", model.SourceIndexProfileName);
    }

    [Fact]
    public void FromDataSource_InferLegacyAzureAISearchApiKeyAuthentication()
    {
        var protector = new EphemeralDataProtectionProvider().CreateProtector(AIDataSourceProtectionConstants.SourceSecretPurpose);
        var dataSource = new AIDataSource
        {
            Source = AIDataSourceSourceTypes.AzureAISearch,
        };
        dataSource.Put(new AzureAISearchSourceMetadata
        {
            Endpoint = "https://kb.search.windows.net",
            IndexName = "kb",
            ApiKey = protector.Protect("legacy-secret"),
        });

        var model = AIDataSourceViewModel.FromDataSource(dataSource);

        Assert.Equal(AzureAISearchSourceMetadata.ApiKeyAuthenticationType, model.AzureAISearchAuthenticationType);
        Assert.Equal("https://kb.search.windows.net", model.AzureAISearchEndpoint);
        Assert.Equal("kb", model.AzureAISearchIndexName);
    }

    [Fact]
    public void ApplyTo_DisablesElasticsearchBasicCredentialsWhenAuthenticationIsNone()
    {
        var protector = new EphemeralDataProtectionProvider().CreateProtector(AIDataSourceProtectionConstants.SourceSecretPurpose);
        var dataSource = new AIDataSource
        {
            Source = AIDataSourceSourceTypes.Elasticsearch,
        };
        dataSource.Put(new ElasticsearchSourceMetadata
        {
            AuthenticationType = ElasticsearchSourceMetadata.BasicAuthenticationType,
            Username = "elastic",
            Password = protector.Protect("secret"),
        });

        var model = new AIDataSourceViewModel
        {
            Source = AIDataSourceSourceTypes.Elasticsearch,
            ElasticsearchEnvironmentType = ElasticsearchSourceMetadata.SelfManagedEnvironmentType,
            ElasticsearchAuthenticationType = ElasticsearchSourceMetadata.NoneAuthenticationType,
            ElasticsearchUrl = "https://cluster",
            ElasticsearchIndexName = "docs",
        };

        model.ApplyTo(dataSource, protector);

        Assert.True(dataSource.TryGet<ElasticsearchSourceMetadata>(out var metadata));
        Assert.Equal(ElasticsearchSourceMetadata.NoneAuthenticationType, metadata.AuthenticationType);
        Assert.Null(metadata.Username);
        Assert.Null(metadata.Password);
    }

    [Fact]
    public void ApplyTo_PreservesExistingElasticsearchBase64ApiKeyWhenBlank()
    {
        var protector = new EphemeralDataProtectionProvider().CreateProtector(AIDataSourceProtectionConstants.SourceSecretPurpose);
        var dataSource = new AIDataSource
        {
            Source = AIDataSourceSourceTypes.Elasticsearch,
        };
        dataSource.Put(new ElasticsearchSourceMetadata
        {
            AuthenticationType = ElasticsearchSourceMetadata.Base64ApiKeyAuthenticationType,
            Base64ApiKey = protector.Protect("ZXhhbXBsZTprZXk="),
        });

        var model = new AIDataSourceViewModel
        {
            Source = AIDataSourceSourceTypes.Elasticsearch,
            ElasticsearchEnvironmentType = ElasticsearchSourceMetadata.CloudHostedEnvironmentType,
            ElasticsearchCloudId = "deployment-name:dXMtZWFzdC0xJGFiYyRkZWY=",
            ElasticsearchAuthenticationType = ElasticsearchSourceMetadata.Base64ApiKeyAuthenticationType,
            ElasticsearchIndexName = "docs",
        };

        model.ApplyTo(dataSource, protector);

        Assert.True(dataSource.TryGet<ElasticsearchSourceMetadata>(out var metadata));
        Assert.Equal(ElasticsearchSourceMetadata.Base64ApiKeyAuthenticationType, metadata.AuthenticationType);
        Assert.Equal("deployment-name:dXMtZWFzdC0xJGFiYyRkZWY=", metadata.CloudId);
        Assert.Equal("ZXhhbXBsZTprZXk=", protector.Unprotect(metadata.Base64ApiKey));
    }

    [Fact]
    public void FromDataSource_InferLegacyElasticsearchCloudEnvironment()
    {
        var dataSource = new AIDataSource
        {
            Source = AIDataSourceSourceTypes.Elasticsearch,
        };
        dataSource.Put(new ElasticsearchSourceMetadata
        {
            CloudId = "deployment-name:dXMtZWFzdC0xJGFiYyRkZWY=",
            IndexName = "docs",
        });

        var model = AIDataSourceViewModel.FromDataSource(dataSource);

        Assert.Equal(ElasticsearchSourceMetadata.CloudHostedEnvironmentType, model.ElasticsearchEnvironmentType);
        Assert.Equal("deployment-name:dXMtZWFzdC0xJGFiYyRkZWY=", model.ElasticsearchCloudId);
    }
}
