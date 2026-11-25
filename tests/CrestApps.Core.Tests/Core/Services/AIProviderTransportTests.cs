using System.Net;
using System.Text;
using CrestApps.Core.AI.AzureAIInference;
using CrestApps.Core.AI.AzureAIInference.Services;
using CrestApps.Core.AI.Claude;
using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.AI.Claude.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Ollama;
using CrestApps.Core.AI.Ollama.Services;
using CrestApps.Core.AI.OpenAI;
using CrestApps.Core.AI.OpenAI.Azure;
using CrestApps.Core.AI.OpenAI.Azure.Models;
using CrestApps.Core.AI.OpenAI.Azure.Services;
using CrestApps.Core.AI.OpenAI.Services;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// The AI provider SDK clients send their requests through a named <see cref="HttpClient"/> the host can configure, and
/// each service container, and so each tenant, builds and keeps its own SDK clients (CrestApps.Core#337).
/// </summary>
public sealed class AIProviderTransportTests
{
    private const string ChatCompletionResponse = """
        {
          "id": "chatcmpl-test",
          "object": "chat.completion",
          "created": 1710000000,
          "model": "test-model",
          "choices": [
            {
              "index": 0,
              "message": { "role": "assistant", "content": "done", "refusal": null },
              "finish_reason": "stop",
              "logprobs": null
            }
          ],
          "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
        }
        """;

    [Fact]
    public async Task OpenAI_SendsRequestsThroughTheHostTransport()
    {
        var handler = new RecordingHandler(ChatCompletionResponse);
        using var services = BuildServices(
            services => services.AddCoreAIOpenAI(),
            OpenAIConstants.HttpClientName,
            handler);
        var provider = ActivatorUtilities.CreateInstance<OpenAIClientProvider>(services);
        var connection = Connection(("Endpoint", "https://unit.test/v1/"), ("ApiKey", "sk-test"), (OpenAIChatApis.SettingName, OpenAIChatApis.ChatCompletions));

        var client = await provider.GetChatClientAsync(connection, "test-model");
        var response = await client.GetResponseAsync("Hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("done", response.Text);
        Assert.Equal("https://unit.test/v1/chat/completions", Assert.Single(handler.RequestUris).AbsoluteUri);
    }

    [Fact]
    public async Task AzureOpenAI_SendsRequestsThroughTheHostTransport()
    {
        var handler = new RecordingHandler(ChatCompletionResponse);
        using var services = BuildServices(
            services => services.AddCoreAIAzureOpenAI(),
            AzureOpenAIConstants.HttpClientName,
            handler);
        var provider = ActivatorUtilities.CreateInstance<AzureOpenAIClientProvider>(services, AzureOptions());
        var connection = Connection(
            ("Endpoint", "https://unit.test/"),
            ("AuthenticationType", "ApiKey"),
            ("ApiKey", "azure-test"),
            (OpenAIChatApis.SettingName, OpenAIChatApis.ChatCompletions));

        var client = await provider.GetChatClientAsync(connection, "test-model");
        var response = await client.GetResponseAsync("Hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("done", response.Text);
        Assert.Equal("unit.test", Assert.Single(handler.RequestUris).Host);
    }

    [Fact]
    public async Task AzureAIInference_SendsRequestsThroughTheHostTransport()
    {
        var handler = new RecordingHandler(ChatCompletionResponse);
        using var services = BuildServices(
            services => services.AddCoreAIAzureAIInference(),
            AzureAIInferenceConstants.HttpClientName,
            handler);
        var provider = ActivatorUtilities.CreateInstance<AzureAIInferenceClientProvider>(services);
        var connection = Connection(("Endpoint", "https://unit.test/"), ("AuthenticationType", "ApiKey"), ("ApiKey", "inference-test"));

        var client = await provider.GetChatClientAsync(connection, "test-model");
        var response = await client.GetResponseAsync("Hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("done", response.Text);
        Assert.Equal("unit.test", Assert.Single(handler.RequestUris).Host);
    }

    [Fact]
    public async Task Ollama_SendsRequestsThroughTheHostTransport()
    {
        var handler = new RecordingHandler("""{ "model": "test-model", "embeddings": [[0.5, 0.25]] }""");
        using var services = BuildServices(
            services => services.AddCoreAIOllama(),
            OllamaConstants.HttpClientName,
            handler);
        var provider = ActivatorUtilities.CreateInstance<OllamaAIClientProvider>(services);
        var connection = Connection(("Endpoint", "http://ollama.test:11434/"));

        var generator = await provider.GetEmbeddingGeneratorAsync(connection, "test-model");
        var embeddings = await generator.GenerateAsync(["Hello"], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new[] { 0.5f, 0.25f }, Assert.Single(embeddings).Vector.ToArray());
        Assert.Equal("http://ollama.test:11434/api/embed", Assert.Single(handler.RequestUris).AbsoluteUri);
    }

    [Fact]
    public async Task Claude_SendsRequestsThroughTheHostTransport()
    {
        var handler = new RecordingHandler("""{ "data": [], "has_more": false, "first_id": null, "last_id": null }""");
        using var services = BuildServices(
            services => services.AddCoreAIClaudeOrchestrator(),
            ClaudeClientService.HttpClientName,
            handler);
        var options = Mock.Of<IOptionsSnapshot<ClaudeOptions>>(snapshot => snapshot.Value == new ClaudeOptions
        {
            ApiKey = "claude-test",
            BaseUrl = "https://anthropic.test",
        });
        var clientService = new ClaudeClientService(options, services.GetRequiredService<IHttpClientFactory>(), NullLogger<ClaudeClientService>.Instance);

        await clientService.ListModelsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("anthropic.test", Assert.Single(handler.RequestUris).Host);
    }

    [Fact]
    public async Task OpenAI_EachContainerSendsThroughItsOwnTransport()
    {
        var firstHandler = new RecordingHandler(ChatCompletionResponse);
        var secondHandler = new RecordingHandler(ChatCompletionResponse);
        using var firstServices = BuildServices(services => services.AddCoreAIOpenAI(), OpenAIConstants.HttpClientName, firstHandler);
        using var secondServices = BuildServices(services => services.AddCoreAIOpenAI(), OpenAIConstants.HttpClientName, secondHandler);
        var connection = Connection(("Endpoint", "https://unit.test/v1/"), ("ApiKey", "sk-shared"), (OpenAIChatApis.SettingName, OpenAIChatApis.ChatCompletions));

        foreach (var services in new[] { firstServices, secondServices })
        {
            var client = await ActivatorUtilities.CreateInstance<OpenAIClientProvider>(services).GetChatClientAsync(connection, "test-model");

            await client.GetResponseAsync("Hello", cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Single(firstHandler.RequestUris);
        Assert.Single(secondHandler.RequestUris);
    }

    [Fact]
    public void AzureOpenAIClientFactory_GivesEachApiKeyOnAnEndpointItsOwnClient()
    {
        using var services = new ServiceCollection().AddCoreAIAzureOpenAI().BuildServiceProvider();
        var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
        var clientCache = services.GetRequiredService<AIProviderClientCache>();
        var firstTenant = Connection(("Endpoint", "https://shared.openai.azure.com/"), ("AuthenticationType", "ApiKey"), ("ApiKey", "first-key"));
        var secondTenant = Connection(("Endpoint", "https://shared.openai.azure.com/"), ("AuthenticationType", "ApiKey"), ("ApiKey", "second-key"));

        var firstClient = AzureOpenAIClientFactory.Create(firstTenant, NullLoggerFactory.Instance, new AzureClientOptions(), httpClientFactory, clientCache);
        var secondClient = AzureOpenAIClientFactory.Create(secondTenant, NullLoggerFactory.Instance, new AzureClientOptions(), httpClientFactory, clientCache);

        Assert.NotSame(firstClient, secondClient);
        Assert.Same(firstClient, AzureOpenAIClientFactory.Create(firstTenant, NullLoggerFactory.Instance, new AzureClientOptions(), httpClientFactory, clientCache));
    }

    [Fact]
    public void ClientCache_ReusesTheClientBuiltForAKey()
    {
        using var cache = new AIProviderClientCache();
        var builds = 0;

        var first = cache.GetOrAdd("key", () =>
        {
            builds++;

            return new DisposableClient();
        });
        var second = cache.GetOrAdd("key", () =>
        {
            builds++;

            return new DisposableClient();
        });

        Assert.Same(first, second);
        Assert.Equal(1, builds);
        Assert.NotSame(first, cache.GetOrAdd("other-key", () => new DisposableClient()));
    }

    [Fact]
    public void ClientCache_KeepsClientsOfDifferentTypesApart()
    {
        using var cache = new AIProviderClientCache();

        var disposableClient = cache.GetOrAdd("key", () => new DisposableClient());
        var otherClient = cache.GetOrAdd("key", () => new object());

        Assert.IsType<DisposableClient>(disposableClient);
        Assert.IsNotType<DisposableClient>(otherClient);
    }

    [Fact]
    public void ClientCache_Clear_DisposesTheClientsAndBuildsNewOnes()
    {
        using var cache = new AIProviderClientCache();
        var first = cache.GetOrAdd("key", () => new DisposableClient());

        cache.Clear();

        Assert.True(first.IsDisposed);
        Assert.NotSame(first, cache.GetOrAdd("key", () => new DisposableClient()));
    }

    [Fact]
    public void ClientCache_DisposingTheContainer_DisposesItsClients()
    {
        var services = new ServiceCollection().AddCoreAIOllama().BuildServiceProvider();
        var client = services.GetRequiredService<AIProviderClientCache>().GetOrAdd("key", () => new DisposableClient());

        services.Dispose();

        Assert.True(client.IsDisposed);
    }

    [Fact]
    public void ClientCache_HashSecret_TellsSecretsApartWithoutHoldingThem()
    {
        var hash = AIProviderClientCache.HashSecret("sk-secret");

        Assert.DoesNotContain("sk-secret", hash, StringComparison.Ordinal);
        Assert.Equal(hash, AIProviderClientCache.HashSecret("sk-secret"));
        Assert.NotEqual(hash, AIProviderClientCache.HashSecret("sk-other"));
        Assert.Equal(AIProviderClientCache.HashSecret(string.Empty), AIProviderClientCache.HashSecret(null));
    }

    [Theory]
    [InlineData(OpenAIConstants.HttpClientName)]
    [InlineData(AzureOpenAIConstants.HttpClientName)]
    [InlineData(AzureAIInferenceConstants.HttpClientName)]
    [InlineData(ClaudeClientService.HttpClientName)]
    public void ProviderHttpClient_IsSetUpLikeTheSdkTransports(string name)
    {
        using var services = new ServiceCollection()
            .AddCoreAIOpenAI()
            .AddCoreAIAzureOpenAI()
            .AddCoreAIAzureAIInference()
            .AddCoreAIClaudeOrchestrator()
            .BuildServiceProvider();

        var primaryHandler = Assert.IsType<SocketsHttpHandler>(GetPrimaryHandler(services, name));

        Assert.Equal(Timeout.InfiniteTimeSpan, services.GetRequiredService<IHttpClientFactory>().CreateClient(name).Timeout);
        Assert.False(primaryHandler.AllowAutoRedirect);
        Assert.False(primaryHandler.UseCookies);
    }

    [Fact]
    public void ProviderHttpClient_ForClaude_DecompressesResponsesLikeTheAnthropicSdk()
    {
        using var services = new ServiceCollection().AddCoreAIClaudeOrchestrator().BuildServiceProvider();

        var primaryHandler = Assert.IsType<SocketsHttpHandler>(GetPrimaryHandler(services, ClaudeClientService.HttpClientName));

        Assert.Equal(DecompressionMethods.All, primaryHandler.AutomaticDecompression);
    }

    [Fact]
    public void ProviderHttpClient_ForOllama_KeepsTheDefaultClient()
    {
        using var services = new ServiceCollection().AddCoreAIOllama().BuildServiceProvider();

        Assert.Equal(TimeSpan.FromSeconds(100), services.GetRequiredService<IHttpClientFactory>().CreateClient(OllamaConstants.HttpClientName).Timeout);
    }

    private static ServiceProvider BuildServices(Action<IServiceCollection> addProvider, string httpClientName, HttpMessageHandler handler)
    {
        var services = new ServiceCollection();

        addProvider(services);

        // The host's own transport for the provider.
        services.AddHttpClient(httpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);

        return services.BuildServiceProvider();
    }

    private static HttpMessageHandler GetPrimaryHandler(IServiceProvider services, string name)
    {
        var handler = services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(name);

        while (handler is DelegatingHandler delegatingHandler)
        {
            handler = delegatingHandler.InnerHandler;
        }

        return handler;
    }

    private static IOptionsSnapshot<AzureClientOptions> AzureOptions()
        => Mock.Of<IOptionsSnapshot<AzureClientOptions>>(snapshot => snapshot.Value == new AzureClientOptions { EnableDefaultRetryPolicy = false });

    private static AIProviderConnectionEntry Connection(params (string Key, string Value)[] values)
        => new(values.ToDictionary(pair => pair.Key, object (pair) => pair.Value));

    /// <summary>
    /// Answers every request with the same JSON body and records where each request was sent.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public RecordingHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>
    /// A client that records whether it was disposed.
    /// </summary>
    private sealed class DisposableClient : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
