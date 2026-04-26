using System.ClientModel;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CrestApps.Core.AI.OpenAI.Services;

/// <summary>
/// Represents the open AI Client Provider.
/// </summary>
public sealed class OpenAIClientProvider : AIClientProviderBase
{
    private static readonly ConcurrentDictionary<string, OpenAIClient> _clientCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIClientProvider"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    public OpenAIClientProvider(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
    }

    /// <summary>
    /// Gets provider name.
    /// </summary>
    protected override string GetProviderName()
    {
        return OpenAIConstants.ClientName;
    }

    /// <summary>
    /// Gets chat client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected override IChatClient GetChatClient(AIProviderConnectionEntry connection, string deploymentName)
    {
        var client = GetOpenAIClient(connection);

        return client.GetChatClient(deploymentName).AsIChatClient();
    }

    /// <summary>
    /// Gets embedding generator.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected override IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator(AIProviderConnectionEntry connection, string deploymentName)
    {
        var client = GetOpenAIClient(connection);

        return client.GetEmbeddingClient(deploymentName).AsIEmbeddingGenerator();
    }
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    /// <summary>
    /// Gets image generator.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected override IImageGenerator GetImageGenerator(AIProviderConnectionEntry connection, string deploymentName)
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    {
        var client = GetOpenAIClient(connection);
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        return client.GetImageClient(deploymentName).AsIImageGenerator();
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
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
        var client = GetOpenAIClient(connection);
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        return client.GetAudioClient(deploymentName).AsISpeechToTextClient();
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    }

    private static OpenAIClient GetOpenAIClient(AIProviderConnectionEntry connection)
    {
        var apiKey = connection.GetApiKey();
        var endpoint = connection.GetEndpoint(false);
        var cacheKey = BuildCacheKey(endpoint, apiKey);

        return _clientCache.GetOrAdd(cacheKey, _ =>
        {
            if (endpoint is null)
            {
                return new OpenAIClient(apiKey);
            }

            return new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = endpoint, });
        });
    }

    /// <summary>
    /// Clears the cached OpenAI client instances, forcing new clients to be created on next use.
    /// Useful when credentials are rotated.
    /// </summary>
    public static void ClearCache()
        => _clientCache.Clear();

    private static string BuildCacheKey(Uri endpoint, string apiKey)
    {
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey ?? string.Empty));
        var keyHash = Convert.ToHexStringLower(keyBytes);

        return $"{endpoint?.AbsoluteUri}|{keyHash}";
    }
}
