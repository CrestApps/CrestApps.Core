using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure;
using Azure.Core.Pipeline;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using CrestApps.Core.AI.Azure.AISearch;
using CrestApps.Core.AI.Elasticsearch;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.Azure.AISearch;
using CrestApps.Core.Azure.AISearch.Services;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Covers the registrations that let a host supply its own Azure AI Search or Elasticsearch client, such as a
/// per-tenant client, instead of the client CrestApps.Core builds from its own connection options.
/// </summary>
public sealed class HostSuppliedSearchClientTests
{
    private static readonly float[] _embedding = [0.1f, 0.2f, 0.3f];

    private static readonly SearchIndexProfile _indexProfile = new()
    {
        IndexName = "docs",
        IndexFullName = "tenant1_docs",
    };

    [Fact]
    public async Task AzureAISearchVectorSearchService_ResolvesItsClientThroughTheFactory_AndEscapesTheFilter()
    {
        // Arrange
        var handler = new CapturingHandler("""{"value":[]}""");
        var factoryCalls = 0;
        var services = new ServiceCollection().AddLogging();

        services.AddAzureAISearchVectorSearchService(_ =>
        {
            factoryCalls++;

            return CreateSearchIndexClient(handler);
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var service = scope.ServiceProvider.GetRequiredKeyedService<IVectorSearchService>(AISearchConstants.ProviderName);
        await service.SearchAsync(_indexProfile, _embedding, "O'Brien", "doc", 3, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, factoryCalls);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("/indexes('tenant1_docs')/docs/search.post.search", request.Uri, StringComparison.Ordinal);
        Assert.Equal("referenceId eq 'O''Brien' and referenceType eq 'doc'", ReadAzureFilter(request.Body));
    }

    [Fact]
    public async Task AzureAISearchVectorSearchService_WithColumnNames_UsesTheHostFieldNames()
    {
        // Arrange
        var handler = new CapturingHandler("""{"value":[]}""");
        var services = new ServiceCollection().AddLogging();

        services.AddAzureAISearchVectorSearchService(
            _ => CreateSearchIndexClient(handler),
            new DocumentIndexColumnNames
            {
                ReferenceId = "ref_id",
                ReferenceType = "ref_type",
                Embedding = "vector",
            });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var service = scope.ServiceProvider.GetRequiredKeyedService<IVectorSearchService>(AISearchConstants.ProviderName);
        await service.SearchAsync(_indexProfile, _embedding, "item-1", "doc", 3, TestContext.Current.CancellationToken);

        // Assert
        var body = JsonNode.Parse(Assert.Single(handler.Requests).Body)!;

        Assert.Equal("ref_id eq 'item-1' and ref_type eq 'doc'", body["filter"]!.GetValue<string>());
        Assert.Equal("vector", body["vectorQueries"]![0]!["fields"]!.GetValue<string>());
        Assert.Equal("chunkId,documentId,content,fileName,chunkIndex", body["select"]!.GetValue<string>());
    }

    [Fact]
    public async Task AzureAISearchMemoryVectorSearchService_ResolvesItsClientThroughTheFactory_AndEscapesTheUserId()
    {
        // Arrange
        var handler = new CapturingHandler("""{"value":[]}""");
        var services = new ServiceCollection().AddLogging();

        services.AddAzureAISearchMemoryVectorSearchService(_ => CreateSearchIndexClient(handler));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var service = scope.ServiceProvider.GetRequiredKeyedService<IMemoryVectorSearchService>(AISearchConstants.ProviderName);
        await service.SearchAsync(_indexProfile, _embedding, "x' or userId ne 'x", 3, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("userId eq 'x'' or userId ne ''x'", ReadAzureFilter(Assert.Single(handler.Requests).Body));
    }

    [Fact]
    public async Task AzureAISearchDataSourceContentManager_ResolvesItsClientThroughTheFactory_AndEscapesTheDataSourceId()
    {
        // Arrange
        var handler = new CapturingHandler("""{"value":[]}""");
        var services = new ServiceCollection().AddLogging();

        services.AddAzureAISearchDataSourceContentManager(_ => CreateSearchIndexClient(handler));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var contentManager = scope.ServiceProvider.GetRequiredKeyedService<IDataSourceContentManager>(AISearchConstants.ProviderName);
        await contentManager.SearchAsync(_indexProfile, _embedding, "it's", 3, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("dataSourceId eq 'it''s'", ReadAzureFilter(Assert.Single(handler.Requests).Body));
    }

    [Fact]
    public void AddCoreAzureAISearchServices_WithClientFactory_RegistersEveryServiceWithoutTheConfiguredClient()
    {
        // Arrange
        var handler = new CapturingHandler("""{"value":[]}""");
        var services = new ServiceCollection().AddLogging();

        // Act
        services.AddCoreAzureAISearchServices(_ => CreateSearchIndexClient(handler));

        // Assert
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IAzureAISearchClientFactory));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(SearchIndexClient));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredKeyedService<IDataSourceContentManager>(AISearchConstants.ProviderName));
        Assert.NotNull(scope.ServiceProvider.GetRequiredKeyedService<IDataSourceDocumentReader>(AISearchConstants.ProviderName));
        Assert.NotNull(scope.ServiceProvider.GetRequiredKeyedService<ISearchIndexManager>(AISearchConstants.ProviderName));
        Assert.NotNull(scope.ServiceProvider.GetRequiredKeyedService<ISearchDocumentManager>(AISearchConstants.ProviderName));
        Assert.NotNull(scope.ServiceProvider.GetRequiredKeyedService<IODataFilterTranslator>(AISearchConstants.ProviderName));
    }

    /// <summary>
    /// Verifies that the registrations that existed before the client-factory overloads still build their
    /// clients through <see cref="IAzureAISearchClientFactory"/>.
    /// </summary>
    [Fact]
    public async Task AzureAISearch_DefaultRegistrations_StillUseTheConfiguredClientFactory()
    {
        // Arrange
        var handler = new CapturingHandler("""{"value":[]}""");
        var clientFactory = new RecordingAzureAISearchClientFactory(CreateSearchIndexClient(handler));
        var services = new ServiceCollection().AddLogging();

        services.AddSingleton<IAzureAISearchClientFactory>(clientFactory);
        services.AddCoreAzureAISearchServices();
        services.AddCoreAzureAISearchAIDocumentSource();
        services.AddCoreAzureAISearchAIMemorySource();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var vectorSearch = scope.ServiceProvider.GetRequiredKeyedService<IVectorSearchService>(AISearchConstants.ProviderName);
        var memorySearch = scope.ServiceProvider.GetRequiredKeyedService<IMemoryVectorSearchService>(AISearchConstants.ProviderName);
        var contentManager = scope.ServiceProvider.GetRequiredKeyedService<IDataSourceContentManager>(AISearchConstants.ProviderName);

        await vectorSearch.SearchAsync(_indexProfile, _embedding, "item-1", "doc", 3, TestContext.Current.CancellationToken);
        await memorySearch.SearchAsync(_indexProfile, _embedding, "user-1", 3, TestContext.Current.CancellationToken);
        await contentManager.SearchAsync(_indexProfile, _embedding, "ds1", 3, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(clientFactory.SearchIndexClientRequests > 0);
        Assert.Equal(
            [
                "referenceId eq 'item-1' and referenceType eq 'doc'",
                "userId eq 'user-1'",
                "dataSourceId eq 'ds1'",
            ],
            handler.Requests.Select(request => ReadAzureFilter(request.Body)));
    }

    /// <summary>
    /// Verifies that a service the host registers with its own client is kept when the feature registration
    /// runs afterwards, so a host can combine the two.
    /// </summary>
    [Fact]
    public void AddAzureAISearchVectorSearchService_BeforeTheFeatureRegistration_KeepsTheHostService()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddAzureAISearchVectorSearchService(_ => throw new InvalidOperationException("Not resolved in this test."));
        services.AddCoreAzureAISearchAIDocumentSource();

        // Assert
        Assert.Single(services, descriptor =>
            descriptor.IsKeyedService &&
            Equals(descriptor.ServiceKey, AISearchConstants.ProviderName) &&
            descriptor.ServiceType == typeof(IVectorSearchService));
    }

    [Fact]
    public async Task ElasticsearchVectorSearchService_ResolvesItsClientThroughTheFactory_WithTheHostFieldNames()
    {
        // Arrange
        var requests = new List<string>();
        var factoryCalls = 0;
        var services = new ServiceCollection().AddLogging();

        services.AddElasticsearchVectorSearchService(
            _ =>
            {
                factoryCalls++;

                return CreateElasticsearchClient(requests);
            },
            new DocumentIndexColumnNames
            {
                ReferenceId = "ref_id",
                Embedding = "vector",
            });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var service = scope.ServiceProvider.GetRequiredKeyedService<IVectorSearchService>(ElasticsearchConstants.ProviderName);
        await service.SearchAsync(_indexProfile, _embedding, "item-1", "doc", 3, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, factoryCalls);

        var knn = JsonNode.Parse(Assert.Single(requests))!["knn"]!;

        if (knn is JsonArray knnArray)
        {
            knn = Assert.Single(knnArray)!;
        }

        Assert.Equal("vector", knn["field"]!.GetValue<string>());

        var terms = knn["filter"]!.ToJsonString();

        Assert.Contains("\"ref_id\"", terms, StringComparison.Ordinal);
        Assert.Contains("\"referenceType\"", terms, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ElasticsearchMemoryVectorSearchService_ResolvesItsClientThroughTheFactory()
    {
        // Arrange
        var requests = new List<string>();
        var factoryCalls = 0;
        var services = new ServiceCollection().AddLogging();

        services.AddElasticsearchMemoryVectorSearchService(_ =>
        {
            factoryCalls++;

            return CreateElasticsearchClient(requests);
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var service = scope.ServiceProvider.GetRequiredKeyedService<IMemoryVectorSearchService>(ElasticsearchConstants.ProviderName);
        await service.SearchAsync(_indexProfile, _embedding, "user-1", 3, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, factoryCalls);
        Assert.Single(requests);
    }

    [Fact]
    public void AddCoreElasticsearchServices_WithClientFactory_RegistersEveryServiceWithoutTheConfiguredClient()
    {
        // Arrange
        var factoryCalls = 0;
        var services = new ServiceCollection().AddLogging();

        // Act
        services.AddCoreElasticsearchServices(_ =>
        {
            factoryCalls++;

            return CreateElasticsearchClient([]);
        });

        // Assert
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(CrestApps.Core.Elasticsearch.Services.IElasticsearchClientFactory));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ElasticsearchClient));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredKeyedService<IDataSourceContentManager>(ElasticsearchConstants.ProviderName));
        Assert.NotNull(scope.ServiceProvider.GetRequiredKeyedService<IDataSourceDocumentReader>(ElasticsearchConstants.ProviderName));
        Assert.NotNull(scope.ServiceProvider.GetRequiredKeyedService<ISearchIndexManager>(ElasticsearchConstants.ProviderName));
        Assert.NotNull(scope.ServiceProvider.GetRequiredKeyedService<ISearchDocumentManager>(ElasticsearchConstants.ProviderName));
        Assert.Equal(4, factoryCalls);
    }

    private static SearchIndexClient CreateSearchIndexClient(CapturingHandler handler)
    {
        var options = new SearchClientOptions
        {
            Transport = new HttpClientTransport(new HttpClient(handler)),
        };

        options.Retry.MaxRetries = 0;

        return new SearchIndexClient(new Uri("https://unit-test.search.windows.net"), new AzureKeyCredential("test-key"), options);
    }

    private static ElasticsearchClient CreateElasticsearchClient(List<string> requests)
    {
        var settings = new ElasticsearchClientSettings(new SingleNodePool(new Uri("http://localhost:9200")), new InMemoryRequestInvoker())
            .DisableDirectStreaming()
            .OnRequestCompleted(details =>
            {
                if (details.RequestBodyInBytes is { } body)
                {
                    requests.Add(Encoding.UTF8.GetString(body));
                }
            });

        return new ElasticsearchClient(settings);
    }

    private static string ReadAzureFilter(string body)
    {
        using var document = JsonDocument.Parse(body);

        return document.RootElement.GetProperty("filter").GetString();
    }

    private sealed record CapturedRequest(string Uri, string Body);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public CapturingHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(request.RequestUri!.ToString(), body));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class RecordingAzureAISearchClientFactory : IAzureAISearchClientFactory
    {
        private readonly SearchIndexClient _client;

        public RecordingAzureAISearchClientFactory(SearchIndexClient client)
        {
            _client = client;
        }

        public int SearchIndexClientRequests { get; private set; }

        public SearchIndexClient CreateSearchIndexClient()
        {
            SearchIndexClientRequests++;

            return _client;
        }

        public SearchClient CreateSearchClient(string indexFullName)
            => _client.GetSearchClient(indexFullName);

        public SearchClient CreateSearchClient(string indexFullName, AzureAISearchConnectionOptions configuration)
            => _client.GetSearchClient(indexFullName);
    }
}
