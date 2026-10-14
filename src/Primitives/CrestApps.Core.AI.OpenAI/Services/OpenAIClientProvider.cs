using System.ClientModel;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace CrestApps.Core.AI.OpenAI.Services;

/// <summary>
/// Represents the open AI Client Provider.
/// </summary>
public sealed class OpenAIClientProvider : AIClientProviderBase
{
    private const string OpenAIApiHost = "api.openai.com";

    private static readonly ConcurrentDictionary<string, OpenAIClient> _clientCache = new(StringComparer.Ordinal);

    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIClientProvider"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    public OpenAIClientProvider(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _logger = serviceProvider.GetService<ILogger<OpenAIClientProvider>>();
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
    /// <remarks>
    /// Requests that combine function tools with a reasoning effort are routed to the Responses API on OpenAI's own
    /// endpoint. Other OpenAI-compatible endpoints often have no Responses API, so they use Chat Completions only
    /// unless the connection's <c>ChatApi</c> setting says otherwise.
    /// </remarks>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected override IChatClient GetChatClient(AIProviderConnectionEntry connection, string deploymentName)
    {
        var client = GetOpenAIClient(connection);
        var chatClient = client.GetChatClient(deploymentName).AsIChatClient();

        if (!UseResponsesRouting(connection))
        {
            return chatClient;
        }

#pragma warning disable OPENAI001 // The Responses types from the OpenAI SDK are for evaluation purposes only.
        return new OpenAIReasoningRoutingChatClient(chatClient, client.GetResponsesClient().AsIChatClient(deploymentName));
#pragma warning restore OPENAI001
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

#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only and requires explicit opt-in at each usage site.
#pragma warning disable OPENAI002 // The OpenAI Realtime API is experimental and requires explicit opt-in at each usage site.
    /// <summary>
    /// Gets a real-time client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    public override ValueTask<IRealtimeClient> GetRealtimeClientAsync(AIProviderConnectionEntry connection, string deploymentName = null)
    {
        if (string.IsNullOrEmpty(deploymentName))
        {
            deploymentName = connection.GetStringValue("RealtimeDeploymentName", false);
        }

        if (string.IsNullOrEmpty(deploymentName))
        {
            throw new ArgumentException("A realtime deployment name must be provided, either directly or as a default in the connection settings.");
        }

        var client = GetOpenAIClient(connection);

        return ValueTask.FromResult<IRealtimeClient>(new OpenAIRealtimeClient(client.GetRealtimeClient(), deploymentName));
    }
#pragma warning restore OPENAI002 // The OpenAI Realtime API is experimental and requires explicit opt-in at each usage site.
#pragma warning restore MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only and requires explicit opt-in at each usage site.

    /// <summary>
    /// Gets the supported real-time voices for the OpenAI gpt-realtime models.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    public override Task<SpeechVoice[]> GetRealtimeVoicesAsync(AIProviderConnectionEntry connection, string deploymentName = null)
    {
        return Task.FromResult(OpenAIRealtimeVoices.All);
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

    private bool UseResponsesRouting(AIProviderConnectionEntry connection)
    {
        var chatApi = OpenAIChatApis.GetChatApi(connection, _logger);

        if (chatApi == OpenAIChatApis.ChatCompletions)
        {
            return false;
        }

        if (chatApi == OpenAIChatApis.Responses)
        {
            return true;
        }

        var endpoint = connection.GetEndpoint(false);

        return endpoint is null || IsOpenAIHost(endpoint.Host);
    }

    /// <summary>
    /// Determines whether the host is OpenAI's own API, including its regional hosts such as
    /// <c>eu.api.openai.com</c> for data residency.
    /// </summary>
    /// <param name="host">The endpoint host.</param>
    private static bool IsOpenAIHost(string host)
        => host.Equals(OpenAIApiHost, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + OpenAIApiHost, StringComparison.OrdinalIgnoreCase);

    private static string BuildCacheKey(Uri endpoint, string apiKey)
    {
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey ?? string.Empty));
        var keyHash = Convert.ToHexStringLower(keyBytes);

        return $"{endpoint?.AbsoluteUri}|{keyHash}";
    }
}
