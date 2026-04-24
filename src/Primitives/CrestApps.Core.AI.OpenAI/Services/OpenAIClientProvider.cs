using System.ClientModel;
using System.Collections.Concurrent;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CrestApps.Core.AI.OpenAI.Services;

public sealed class OpenAIClientProvider : AIClientProviderBase
{
    private static readonly ConcurrentDictionary<string, OpenAIClient> _clientCache = new(StringComparer.Ordinal);

    public OpenAIClientProvider(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    protected override string GetProviderName()
    {
        return OpenAIConstants.ClientName;
    }

    protected override IChatClient GetChatClient(AIProviderConnectionEntry connection, string deploymentName)
    {
        var client = GetOpenAIClient(connection);

        return client.GetChatClient(deploymentName).AsIChatClient();
    }

    protected override IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator(AIProviderConnectionEntry connection, string deploymentName)
    {
        var client = GetOpenAIClient(connection);

        return client.GetEmbeddingClient(deploymentName).AsIEmbeddingGenerator();
    }

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    protected override IImageGenerator GetImageGenerator(AIProviderConnectionEntry connection, string deploymentName)
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    {
        var client = GetOpenAIClient(connection);
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        return client.GetImageClient(deploymentName).AsIImageGenerator();
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    }

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

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
        var cacheKey = $"{endpoint?.AbsoluteUri}|{apiKey}";

        return _clientCache.GetOrAdd(cacheKey, _ =>
        {
            if (endpoint is null)
            {
                return new OpenAIClient(apiKey);
            }

            return new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = endpoint, });
        });
    }
}
