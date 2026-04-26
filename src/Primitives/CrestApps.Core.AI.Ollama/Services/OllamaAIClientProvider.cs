using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<string, OllamaApiClient> _clientCache = new(StringComparer.Ordinal);

    public OllamaAIClientProvider(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    protected override string GetProviderName()
    {
        return OllamaConstants.ClientName;
    }

    protected override IChatClient GetChatClient(AIProviderConnectionEntry connection, string deploymentName)
    {
        return GetOllamaClient(connection, deploymentName);
    }

    protected override IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator(AIProviderConnectionEntry connection, string deploymentName)
    {
        return GetOllamaClient(connection, deploymentName);
    }

#pragma warning disable MEAI001
    protected override IImageGenerator GetImageGenerator(AIProviderConnectionEntry connection, string deploymentName)
#pragma warning restore MEAI001
    {
        throw new NotSupportedException("Ollama does not support image generation.");
    }

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    protected override ISpeechToTextClient GetSpeechToTextClient(AIProviderConnectionEntry connection, string deploymentName)
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    {
        throw new NotSupportedException("Ollama does not currently support speech-to-text functionality.");
    }

    /// <summary>
    /// Clears the cached Ollama client instances, forcing new clients to be created on next use.
    /// </summary>
    public static void ClearCache()
    {
        foreach (var client in _clientCache.Values)
        {
            client.Dispose();
        }

        _clientCache.Clear();
    }

    private static OllamaApiClient GetOllamaClient(AIProviderConnectionEntry connection, string deploymentName)
    {
        var endpoint = connection.GetEndpoint();
        var cacheKey = $"{endpoint.AbsoluteUri}|{deploymentName}";

        return _clientCache.GetOrAdd(cacheKey, _ => new OllamaApiClient(endpoint, deploymentName));
    }
}
