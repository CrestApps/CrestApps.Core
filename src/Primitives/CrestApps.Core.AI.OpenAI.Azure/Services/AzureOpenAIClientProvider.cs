using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI.Azure.Models;
using CrestApps.Core.AI.OpenAI.Services;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace CrestApps.Core.AI.OpenAI.Azure.Services;

/// <summary>
/// Represents the azure Open AI Client Provider.
/// </summary>
public sealed class AzureOpenAIClientProvider : AIClientProviderBase
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly AzureClientOptions _azureClientSettings;

    /// <summary>
    /// Gets provider name.
    /// </summary>
    protected override string GetProviderName()
    {
        return AzureOpenAIConstants.ClientName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureOpenAIClientProvider"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="azureClientSettings">The azure client settings.</param>
    public AzureOpenAIClientProvider(
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        IOptionsSnapshot<AzureClientOptions> azureClientSettings)
        : base(serviceProvider)
    {
        _loggerFactory = loggerFactory;
        _azureClientSettings = azureClientSettings.Value;
    }

    /// <summary>
    /// Gets chat client.
    /// </summary>
    /// <remarks>
    /// Requests that combine function tools with a reasoning effort are sent to the Responses API, unless the
    /// connection's <c>ChatApi</c> setting is <c>ChatCompletions</c>.
    /// </remarks>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected override IChatClient GetChatClient(AIProviderConnectionEntry connection, string deploymentName)
    {
        var client = GetClient(connection);
        var chatClient = client.GetChatClient(deploymentName).AsIChatClient();

        if (OpenAIChatApis.GetChatApi(connection, _loggerFactory.CreateLogger<AzureOpenAIClientProvider>()) == OpenAIChatApis.ChatCompletions)
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
        return GetClient(connection)
            .GetEmbeddingClient(deploymentName)
            .AsIEmbeddingGenerator();
    }

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    /// <summary>
    /// Gets image generator.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected override IImageGenerator GetImageGenerator(AIProviderConnectionEntry connection, string deploymentName)
    {
        return GetClient(connection)
            .GetImageClient(deploymentName)
            .AsIImageGenerator();
    }

    /// <summary>
    /// Gets speech to text client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected override ISpeechToTextClient GetSpeechToTextClient(AIProviderConnectionEntry connection, string deploymentName)
    {
        return GetClient(connection)
            .GetAudioClient(deploymentName)
            .AsISpeechToTextClient();
    }
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only and requires explicit opt-in at each usage site.
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

        return ValueTask.FromResult<IRealtimeClient>(new OpenAIRealtimeClient(AzureOpenAIClientFactory.CreateRealtimeClient(connection), deploymentName));
    }
#pragma warning restore MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only and requires explicit opt-in at each usage site.

    /// <summary>
    /// Gets the supported real-time voices for the Azure OpenAI gpt-realtime models.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    public override Task<SpeechVoice[]> GetRealtimeVoicesAsync(AIProviderConnectionEntry connection, string deploymentName = null)
    {
        return Task.FromResult(OpenAIRealtimeVoices.All);
    }

    private OpenAIClient GetClient(AIProviderConnectionEntry connection)
    {
        return AzureOpenAIClientFactory.Create(connection, _loggerFactory, _azureClientSettings);
    }
}
