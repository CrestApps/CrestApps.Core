using Azure.AI.OpenAI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI.Azure.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected override IChatClient GetChatClient(AIProviderConnectionEntry connection, string deploymentName)
    {
        return GetClient(connection)
            .GetChatClient(deploymentName)
            .AsIChatClient();
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
    /// <remarks>
    /// This uses a temporary WebSocket transport (<c>CrestApps.Core.AI.OpenAI.Azure.Realtime</c>) rather than
    /// <c>AzureOpenAIClient.GetRealtimeClient()</c>, because the pinned <c>Azure.AI.OpenAI</c> targets an older
    /// <c>OpenAI</c> SDK than <c>Microsoft.Extensions.AI.OpenAI</c> requires and the SDK path throws
    /// <see cref="MissingMethodException"/> at runtime. Replace this with the SDK path once a compatible
    /// <c>Azure.AI.OpenAI</c> ships.
    /// </remarks>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    public override ValueTask<IRealtimeClient> GetRealtimeClientAsync(AIProviderConnectionEntry connection, string deploymentName = null)
    {
        return ValueTask.FromResult(Realtime.AzureRealtimeClientFactory.Create(connection, deploymentName));
    }
#pragma warning restore MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only and requires explicit opt-in at each usage site.

    private AzureOpenAIClient GetClient(AIProviderConnectionEntry connection)
    {
        return AzureOpenAIClientFactory.Create(connection, _loggerFactory, _azureClientSettings);
    }
}
