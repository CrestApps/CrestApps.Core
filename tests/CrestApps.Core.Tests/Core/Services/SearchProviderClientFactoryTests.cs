using System.Text;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Azure.AISearch;
using CrestApps.Core.Azure.AISearch.Services;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Elasticsearch.Services;
using Elastic.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
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
    public void ElasticsearchCreate_ShouldRequireUrlOrCloudId()
    {
        var factory = new ElasticsearchClientFactory(
            NullLogger<ElasticsearchClientFactory>.Instance,
            Options.Create(new ElasticsearchConnectionOptions()));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Create());

        Assert.Contains("URL or the Cloud ID", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ElasticsearchCreate_ShouldRejectIncompleteKeyIdAndKeyAuthentication()
    {
        var factory = new ElasticsearchClientFactory(
            NullLogger<ElasticsearchClientFactory>.Instance,
            Options.Create(new ElasticsearchConnectionOptions
            {
                Url = "https://localhost:9200",
                AuthenticationType = ElasticsearchSourceMetadata.KeyIdAndKeyAuthenticationType,
                ApiKeyId = "key-id",
            }));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Create());

        Assert.Contains("API key ID and API key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ElasticsearchCreate_ShouldUseBasicAuthenticationHeader()
    {
        var authenticationHeader = ElasticsearchClientFactory.CreateAuthorizationHeader(new ElasticsearchConnectionOptions
        {
            Url = "https://localhost:9200",
            AuthenticationType = ElasticsearchConnectionOptions.BasicAuthenticationType,
            Username = "elastic",
            Password = "secret",
        }, ElasticsearchConnectionOptions.BasicAuthenticationType);

        Assert.NotNull(authenticationHeader);
        Assert.Equal("Basic", authenticationHeader.AuthScheme);
        Assert.True(authenticationHeader.TryGetAuthorizationParameters(out var value));
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("elastic:secret")), value);
    }

    [Fact]
    public void ElasticsearchCreate_ShouldUseApiKeyAuthenticationHeader()
    {
        var authenticationHeader = ElasticsearchClientFactory.CreateAuthorizationHeader(new ElasticsearchConnectionOptions
        {
            Url = "https://localhost:9200",
            AuthenticationType = ElasticsearchConnectionOptions.ApiKeyAuthenticationType,
            ApiKey = "raw-api-key",
        }, ElasticsearchConnectionOptions.ApiKeyAuthenticationType);

        Assert.NotNull(authenticationHeader);
        Assert.Equal("ApiKey", authenticationHeader.AuthScheme);
        Assert.True(authenticationHeader.TryGetAuthorizationParameters(out var value));
        Assert.Equal("raw-api-key", value);
    }

    [Fact]
    public void ElasticsearchCreate_ShouldUseBase64ApiKeyAuthenticationHeader()
    {
        var authenticationHeader = ElasticsearchClientFactory.CreateAuthorizationHeader(new ElasticsearchConnectionOptions
        {
            Url = "https://localhost:9200",
            AuthenticationType = ElasticsearchConnectionOptions.Base64ApiKeyAuthenticationType,
            Base64ApiKey = "YmFzZTY0LWtleQ==",
        }, ElasticsearchConnectionOptions.Base64ApiKeyAuthenticationType);

        Assert.NotNull(authenticationHeader);
        Assert.Equal("ApiKey", authenticationHeader.AuthScheme);
        Assert.True(authenticationHeader.TryGetAuthorizationParameters(out var value));
        Assert.Equal("YmFzZTY0LWtleQ==", value);
    }

    [Fact]
    public void ElasticsearchCreate_ShouldUseKeyIdAndKeyAuthenticationHeader()
    {
        var authenticationHeader = ElasticsearchClientFactory.CreateAuthorizationHeader(new ElasticsearchConnectionOptions
        {
            Url = "https://localhost:9200",
            AuthenticationType = ElasticsearchConnectionOptions.KeyIdAndKeyAuthenticationType,
            ApiKeyId = "key-id",
            ApiKey = "raw-api-key",
        }, ElasticsearchConnectionOptions.KeyIdAndKeyAuthenticationType);

        Assert.NotNull(authenticationHeader);
        Assert.Equal("ApiKey", authenticationHeader.AuthScheme);
        Assert.True(authenticationHeader.TryGetAuthorizationParameters(out var value));
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("key-id:raw-api-key")), value);
    }

    [Fact]
    public void ElasticsearchCreate_ShouldPreferCloudIdWhenBothCloudIdAndUrlAreProvided()
    {
        var factory = new ElasticsearchClientFactory(
            NullLogger<ElasticsearchClientFactory>.Instance,
            Options.Create(new ElasticsearchConnectionOptions
            {
                Url = "not-a-valid-uri",
                CloudId = "deployment-name:dXMtZWFzdC0xJGFiYyRkZWY=",
                AuthenticationType = ElasticsearchConnectionOptions.ApiKeyAuthenticationType,
                ApiKey = "raw-api-key",
            }));

        var client = factory.Create();

        Assert.NotNull(client);
        Assert.Equal(typeof(HttpRequestInvoker), client.Transport.Configuration.RequestInvoker.GetType());
    }

    [Fact]
    public void ElasticsearchCreate_ShouldUseResilientHttpRequestInvoker()
    {
        var client = CreateElasticsearchClient(new ElasticsearchConnectionOptions
        {
            Url = "https://localhost:9200",
        });

        Assert.IsType<HttpRequestInvoker>(client.Transport.Configuration.RequestInvoker);
    }

    [Fact]
    public void ElasticsearchCreateResilientHttpMessageHandler_ShouldWrapWithResilienceHandler()
    {
        var innerHandler = new HttpClientHandler();
        var boundConfiguration = CreateBoundConfiguration();

        var handler = ElasticsearchClientFactory.CreateResilientHttpMessageHandler(innerHandler, boundConfiguration);

        var resilienceHandler = Assert.IsType<ResilienceHandler>(handler);
        Assert.Same(innerHandler, resilienceHandler.InnerHandler);
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

    [Fact]
    public void AzureCreateSearchIndexClient_ShouldRequireApiKeyWhenApiKeyAuthenticationIsConfigured()
    {
        var factory = new AzureAISearchClientFactory(Options.Create(new AzureAISearchConnectionOptions
        {
            Endpoint = "https://example.search.windows.net",
            AuthenticationType = "ApiKey",
        }));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateSearchIndexClient());

        Assert.Contains("admin API key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AzureCreateSearchIndexClient_ShouldAllowDefaultAuthenticationWithoutApiKey()
    {
        var factory = new AzureAISearchClientFactory(Options.Create(new AzureAISearchConnectionOptions
        {
            Endpoint = "https://example.search.windows.net",
            AuthenticationType = "Default",
        }));

        var client = factory.CreateSearchIndexClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void AzureCreateSearchIndexClient_ShouldAllowManagedIdentityAuthenticationWithoutApiKey()
    {
        var factory = new AzureAISearchClientFactory(Options.Create(new AzureAISearchConnectionOptions
        {
            Endpoint = "https://example.search.windows.net",
            AuthenticationType = "ManagedIdentity",
            IdentityClientId = "11111111-1111-1111-1111-111111111111",
        }));

        var client = factory.CreateSearchIndexClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddCoreAzureAISearchServices_ShouldBindConfiguredOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["CrestApps:AzureAISearch:Endpoint"] = "https://example.search.windows.net",
                ["CrestApps:AzureAISearch:IndexPrefix"] = "legacy-",
                ["CrestApps:AzureAISearch:AuthenticationType"] = "ApiKey",
                ["CrestApps:AzureAISearch:ApiKey"] = "test-key",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoreAzureAISearchServices(configuration.GetSection("CrestApps:AzureAISearch"));

        using var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<IOptions<AzureAISearchConnectionOptions>>().Value;
        var client = serviceProvider.GetRequiredService<SearchIndexClient>();

        Assert.Equal("test-key", options.ApiKey);
        Assert.Equal("legacy-", options.GetResolvedIndexPrefix());
        Assert.True(options.UsesApiKeyAuthentication());
        Assert.Equal("ApiKey", options.AuthenticationType);
        Assert.NotNull(client);
    }

    private static Elastic.Clients.Elasticsearch.ElasticsearchClient CreateElasticsearchClient(ElasticsearchConnectionOptions options)
    {
        var factory = new ElasticsearchClientFactory(
            NullLogger<ElasticsearchClientFactory>.Instance,
            Options.Create(options));

        return factory.Create();
    }

    private static BoundConfiguration CreateBoundConfiguration()
    {
        var settings = new Elastic.Clients.Elasticsearch.ElasticsearchClientSettings(new Uri("https://localhost:9200"));

        return new BoundConfiguration(settings, null);
    }
}
