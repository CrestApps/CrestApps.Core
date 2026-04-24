using Azure.Search.Documents;
using CrestApps.Core.Azure.AISearch;
using CrestApps.Core.Azure.AISearch.Services;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Elasticsearch.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class SearchProviderClientFactoryTests
{
    [Fact]
    public void ElasticsearchCreate_ShouldCacheConfiguredClient()
    {
        var factory = new ElasticsearchClientFactory(
            NullLogger<ElasticsearchClientFactory>.Instance,
            Options.Create(new ElasticsearchConnectionOptions
            {
                Url = "https://localhost:9200",
            }));

        var firstClient = factory.Create();
        var secondClient = factory.Create();

        Assert.Same(firstClient, secondClient);
    }

    [Fact]
    public void ElasticsearchCreate_ShouldRejectPartialBasicAuthentication()
    {
        var factory = new ElasticsearchClientFactory(
            NullLogger<ElasticsearchClientFactory>.Instance,
            Options.Create(new ElasticsearchConnectionOptions
            {
                Url = "https://localhost:9200",
                Username = "elastic",
            }));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Create());

        Assert.Contains("username and password", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AzureCreateSearchIndexClient_ShouldCacheConfiguredClient()
    {
        var factory = new AzureAISearchClientFactory(Options.Create(new AzureAISearchConnectionOptions
        {
            Endpoint = "https://example.search.windows.net",
            ApiKey = "test-key",
        }));

        var firstClient = factory.CreateSearchIndexClient();
        var secondClient = factory.CreateSearchIndexClient();

        Assert.Same(firstClient, secondClient);
    }

    [Fact]
    public void AzureCreateSearchClient_ShouldCachePerIndexName()
    {
        var factory = new AzureAISearchClientFactory(Options.Create(new AzureAISearchConnectionOptions
        {
            Endpoint = "https://example.search.windows.net",
            ApiKey = "test-key",
        }));

        SearchClient firstClient = factory.CreateSearchClient("articles");
        SearchClient secondClient = factory.CreateSearchClient("articles");
        SearchClient thirdClient = factory.CreateSearchClient("memory");

        Assert.Same(firstClient, secondClient);
        Assert.NotSame(firstClient, thirdClient);
    }

    [Fact]
    public void AzureCreateSearchClient_ShouldRequireIndexName()
    {
        var factory = new AzureAISearchClientFactory(Options.Create(new AzureAISearchConnectionOptions
        {
            Endpoint = "https://example.search.windows.net",
            ApiKey = "test-key",
        }));

        Assert.Throws<ArgumentException>(() => factory.CreateSearchClient(" "));
    }
}
