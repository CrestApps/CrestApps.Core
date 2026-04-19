using Azure.AI.OpenAI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI.Azure.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.OpenAI.Azure.Services;

public sealed class AzureOpenAIClientProvider : AIClientProviderBase
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly AzureClientOptions _azureClientSettings;

    protected override string GetProviderName()
    {
        return AzureOpenAIConstants.ClientName;
    }

    public AzureOpenAIClientProvider(
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        IOptionsSnapshot<AzureClientOptions> azureClientSettings) : base(serviceProvider)
    {
        _loggerFactory = loggerFactory;
        _azureClientSettings = azureClientSettings.Value;
    }

    protected override IChatClient GetChatClient(AIProviderConnectionEntry connection, string deploymentName)
    {
        return GetClient(connection)
            .GetChatClient(deploymentName)
            .AsIChatClient();
    }

    protected override IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator(AIProviderConnectionEntry connection, string deploymentName)
    {
        return GetClient(connection)
            .GetEmbeddingClient(deploymentName)
            .AsIEmbeddingGenerator();
    }

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    protected override IImageGenerator GetImageGenerator(AIProviderConnectionEntry connection, string deploymentName)
    {
        return GetClient(connection)
            .GetImageClient(deploymentName)
            .AsIImageGenerator();
    }

    protected override ISpeechToTextClient GetSpeechToTextClient(AIProviderConnectionEntry connection, string deploymentName)
    {
        return GetClient(connection)
            .GetAudioClient(deploymentName)
            .AsISpeechToTextClient();
    }

#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    private AzureOpenAIClient GetClient(AIProviderConnectionEntry connection)
    {
        return AzureOpenAIClientFactory.Create(connection, _loggerFactory, _azureClientSettings);
    }
}
