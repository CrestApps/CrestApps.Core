using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace CrestApps.Core.AI.Ollama.Services;

/// <summary>
/// Represents the ollama AI Client Provider.
/// </summary>
public sealed class OllamaAIClientProvider : AIClientProviderBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AIProviderClientCache _clientCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="OllamaAIClientProvider"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="httpClientFactory">The factory of the <see cref="OllamaConstants.HttpClientName"/> client the Ollama client sends its requests through.</param>
    /// <param name="clientCache">The cache that holds the Ollama clients this container has built.</param>
    public OllamaAIClientProvider(
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        AIProviderClientCache clientCache)
        : base(serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
        _clientCache = clientCache;
    }

    /// <summary>
    /// Gets provider name.
    /// </summary>
    protected override string GetProviderName()
    {
        return OllamaConstants.ClientName;
    }

    /// <summary>
    /// Gets chat client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected override IChatClient GetChatClient(AIProviderConnectionEntry connection, string deploymentName)
    {
        return GetOllamaClient(connection, deploymentName);
    }

    /// <summary>
    /// Gets embedding generator.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected override IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator(AIProviderConnectionEntry connection, string deploymentName)
    {
        return GetOllamaClient(connection, deploymentName);
    }

#pragma warning disable MEAI001
    /// <summary>
    /// Gets image generator.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected override IImageGenerator GetImageGenerator(AIProviderConnectionEntry connection, string deploymentName)
#pragma warning restore MEAI001
    {
        throw new NotSupportedException("Ollama does not support image generation.");
    }

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    /// <summary>
    /// Gets speech to text client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected override ISpeechToTextClient GetSpeechToTextClient(AIProviderConnectionEntry connection, string deploymentName)
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    {
        throw new NotSupportedException("Ollama does not currently support speech-to-text functionality.");
    }

    private OllamaApiClient GetOllamaClient(AIProviderConnectionEntry connection, string deploymentName)
    {
        var endpoint = connection.GetEndpoint();

        return _clientCache.GetOrAdd($"{endpoint.AbsoluteUri}|{deploymentName}", () =>
        {
            var httpClient = _httpClientFactory.CreateClient(OllamaConstants.HttpClientName);
            httpClient.BaseAddress = endpoint;

            return new OllamaApiClient(httpClient, deploymentName);
        });
    }
}
