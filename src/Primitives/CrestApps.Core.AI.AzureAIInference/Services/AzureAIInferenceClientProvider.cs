using Azure;
using Azure.AI.Inference;
using Azure.Identity;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Azure;
using CrestApps.Core.Azure.Models;
using CrestApps.Core.Infrastructure;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.AzureAIInference.Services;

/// <summary>
/// Represents the azure AI Inference Client Provider.
/// </summary>
public sealed class AzureAIInferenceClientProvider : AIClientProviderBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AzureAIInferenceClientProvider"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    public AzureAIInferenceClientProvider(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    /// <summary>
    /// Gets provider name.
    /// </summary>
    protected override string GetProviderName()
    {
        return AzureAIInferenceConstants.ClientName;
    }

    /// <summary>
    /// Gets chat client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected override IChatClient GetChatClient(AIProviderConnectionEntry connection, string deploymentName)
    {
        var endpoint = connection.GetEndpoint();
        var identityId = connection.GetIdentityId();
        var client = connection.GetAzureAuthenticationType() switch
        {
            AzureAuthenticationType.ApiKey => new ChatCompletionsClient(endpoint, new AzureKeyCredential(connection.GetApiKey())),
            AzureAuthenticationType.ManagedIdentity => new ChatCompletionsClient(endpoint, new ManagedIdentityCredential(string.IsNullOrEmpty(identityId) ? ManagedIdentityId.SystemAssigned : ManagedIdentityId.FromUserAssignedClientId(identityId))),
            AzureAuthenticationType.Default => new ChatCompletionsClient(endpoint, new DefaultAzureCredential()),
            _ => throw new NotSupportedException("The provided authentication type is not supported."),
        };
        return client.AsIChatClient(deploymentName);
    }

    /// <summary>
    /// Gets embedding generator.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected override IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator(AIProviderConnectionEntry connection, string deploymentName)
    {
        var endpoint = connection.GetEndpoint();
        var identityId = connection.GetIdentityId();
        var client = connection.GetAzureAuthenticationType() switch
        {
            AzureAuthenticationType.ApiKey => new EmbeddingsClient(endpoint, new AzureKeyCredential(connection.GetApiKey())),
            AzureAuthenticationType.ManagedIdentity => new EmbeddingsClient(endpoint, new ManagedIdentityCredential(string.IsNullOrEmpty(identityId) ? ManagedIdentityId.SystemAssigned : ManagedIdentityId.FromUserAssignedClientId(identityId))),
            AzureAuthenticationType.Default => new EmbeddingsClient(endpoint, new DefaultAzureCredential()),
            _ => throw new NotSupportedException("The provided authentication type is not supported."),
        };
        return client.AsIEmbeddingGenerator();
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
        throw new NotSupportedException("Azure AI Inference does not support image generation.");
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
        throw new NotSupportedException("Azure AI Inference does not currently support speech-to-text functionality.");
    }
}
