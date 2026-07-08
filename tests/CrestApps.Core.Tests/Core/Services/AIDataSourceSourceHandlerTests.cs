using CrestApps.Core.AI.Azure.AISearch;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Elasticsearch;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.PostgreSQL;
using CrestApps.Core.Azure.AISearch.Services;
using CrestApps.Core.Elasticsearch.Services;
using CrestApps.Core.Models;
using CrestApps.Core.PostgreSQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class AIDataSourceSourceHandlerTests
{
    [Fact]
    public async Task ElasticsearchHandler_ValidateAsync_ShouldRequirePasswordWhenBasicAuthUsesUsername()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddDataProtection();
        services.AddSingleton(Mock.Of<IElasticsearchClientFactory>());
        services.AddCoreElasticsearchAIDataSource();
        using var serviceProvider = services.BuildServiceProvider();
        var handler = serviceProvider.GetRequiredKeyedService<IAIDataSourceSourceHandler>(AIDataSourceSourceTypes.Elasticsearch);
        var dataSource = new AIDataSource
        {
            SourceType = AIDataSourceSourceTypes.Elasticsearch,
        };
        dataSource.Put(new ElasticsearchSourceMetadata
        {
            Url = "https://elastic.example.com",
            AuthenticationType = ElasticsearchSourceMetadata.BasicAuthenticationType,
            IndexName = "articles",
            Username = "elastic",
        });
        var result = new ValidationResultDetails();

        await handler.ValidateAsync(dataSource, result, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.MemberNames.Contains(nameof(ElasticsearchSourceMetadata.Password)));
    }

    [Fact]
    public async Task AzureAISearchHandler_ValidateAsync_ShouldRequireApiKeyForApiKeyAuthentication()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddDataProtection();
        services.AddSingleton(Mock.Of<IAzureAISearchClientFactory>());
        services.AddCoreAzureAISearchAIDataSource();
        using var serviceProvider = services.BuildServiceProvider();
        var handler = serviceProvider.GetRequiredKeyedService<IAIDataSourceSourceHandler>(AIDataSourceSourceTypes.AzureAISearch);
        var dataSource = new AIDataSource
        {
            SourceType = AIDataSourceSourceTypes.AzureAISearch,
        };
        dataSource.Put(new AzureAISearchSourceMetadata
        {
            Endpoint = "https://example.search.windows.net",
            AuthenticationType = AzureAISearchSourceMetadata.ApiKeyAuthenticationType,
            IndexName = "articles",
        });
        var result = new ValidationResultDetails();

        await handler.ValidateAsync(dataSource, result, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.MemberNames.Contains(nameof(AzureAISearchSourceMetadata.ApiKey)));
    }

    [Fact]
    public async Task AzureAISearchHandler_ValidateAsync_ShouldAllowManagedIdentityWithoutApiKey()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddDataProtection();
        services.AddSingleton(Mock.Of<IAzureAISearchClientFactory>());
        services.AddCoreAzureAISearchAIDataSource();
        using var serviceProvider = services.BuildServiceProvider();
        var handler = serviceProvider.GetRequiredKeyedService<IAIDataSourceSourceHandler>(AIDataSourceSourceTypes.AzureAISearch);
        var dataSource = new AIDataSource
        {
            SourceType = AIDataSourceSourceTypes.AzureAISearch,
        };
        dataSource.Put(new AzureAISearchSourceMetadata
        {
            Endpoint = "https://example.search.windows.net",
            AuthenticationType = AzureAISearchSourceMetadata.ManagedIdentityAuthenticationType,
            IdentityClientId = "managed-identity-client-id",
            IndexName = "articles",
        });
        var result = new ValidationResultDetails();

        await handler.ValidateAsync(dataSource, result, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task PostgreSQLHandler_ValidateAsync_ShouldRequireConnectionStringAndTable()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddDataProtection();
        services.AddSingleton(Mock.Of<IPostgreSQLClientFactory>());
        services.AddCorePostgreSQLAIDataSource();
        using var serviceProvider = services.BuildServiceProvider();
        var handler = serviceProvider.GetRequiredKeyedService<IAIDataSourceSourceHandler>(AIDataSourceSourceTypes.PostgreSQL);
        var dataSource = new AIDataSource
        {
            SourceType = AIDataSourceSourceTypes.PostgreSQL,
        };
        dataSource.Put(new PostgreSQLSourceMetadata());
        var result = new ValidationResultDetails();

        await handler.ValidateAsync(dataSource, result, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.MemberNames.Contains(nameof(PostgreSQLSourceMetadata.ConnectionString)));
        Assert.Contains(result.Errors, error => error.MemberNames.Contains(nameof(PostgreSQLSourceMetadata.TableName)));
    }

    [Fact]
    public void ElasticsearchSourceMetadata_GetAuthenticationType_ShouldInferBasicFromLegacyCredentials()
    {
        var metadata = new ElasticsearchSourceMetadata
        {
            Username = "elastic",
            Password = "protected-secret",
        };

        var authenticationType = metadata.GetAuthenticationType();

        Assert.Equal(ElasticsearchSourceMetadata.BasicAuthenticationType, authenticationType);
    }

    [Fact]
    public void AzureAISearchSourceMetadata_GetAuthenticationType_ShouldInferApiKeyFromLegacyApiKey()
    {
        var metadata = new AzureAISearchSourceMetadata
        {
            ApiKey = "protected-secret",
        };

        var authenticationType = metadata.GetAuthenticationType();

        Assert.Equal(AzureAISearchSourceMetadata.ApiKeyAuthenticationType, authenticationType);
    }
}
