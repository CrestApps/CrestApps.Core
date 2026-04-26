using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Exceptions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Represents the AI Client Provider Base.
/// </summary>
public abstract class AIClientProviderBase : IAIClientProvider
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIClientProviderBase"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    protected AIClientProviderBase(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Determines whether handle.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    public bool CanHandle(string providerName)
    {
        return string.Equals(GetProviderName(), providerName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets chat client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    public ValueTask<IChatClient> GetChatClientAsync(AIProviderConnectionEntry connection, string deploymentName = null)
    {
        if (string.IsNullOrEmpty(deploymentName))
        {
            deploymentName = connection.GetStringValue("ChatDeploymentName", false);
        }

        if (string.IsNullOrEmpty(deploymentName))
        {
            throw new AIDeploymentConfigurationException("A chat deployment name must be provided, either directly or as a default in the connection settings.");
        }

        var client = GetChatClient(connection, deploymentName);
        var builder = new ChatClientBuilder(client);
        return ValueTask.FromResult(builder.Build(_serviceProvider));
    }

    /// <summary>
    /// Gets embedding generator.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> GetEmbeddingGeneratorAsync(AIProviderConnectionEntry connection, string deploymentName = null)
    {
        if (string.IsNullOrEmpty(deploymentName))
        {
            deploymentName = connection.GetStringValue("EmbeddingDeploymentName", false);
        }

        if (string.IsNullOrEmpty(deploymentName))
        {
            throw new ArgumentException("An embedding deployment name must be provided, either directly or as a default in the connection settings.");
        }

        var client = GetEmbeddingGenerator(connection, deploymentName);
        var builder = new EmbeddingGeneratorBuilder<string, Embedding<float>>(client);
        return ValueTask.FromResult(builder.Build(_serviceProvider));
    }
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    /// <summary>
    /// Gets image generator.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    public ValueTask<IImageGenerator> GetImageGeneratorAsync(AIProviderConnectionEntry connection, string deploymentName = null)
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    {
        if (string.IsNullOrEmpty(deploymentName))
        {
            deploymentName = connection.GetStringValue("ImagesDeploymentName", false);
        }

        if (string.IsNullOrEmpty(deploymentName))
        {
            throw new ArgumentException("An image deployment name must be provided, either directly or as a default in the connection settings.");
        }

        var generator = GetImageGenerator(connection, deploymentName);
        return ValueTask.FromResult(generator);
    }
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    /// <summary>
    /// Gets speech to text client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    public ValueTask<ISpeechToTextClient> GetSpeechToTextClientAsync(AIProviderConnectionEntry connection, string deploymentName = null)
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    {
        if (string.IsNullOrEmpty(deploymentName))
        {
            deploymentName = connection.GetStringValue("SpeechToTextDeploymentName", false);
        }

        if (string.IsNullOrEmpty(deploymentName))
        {
            throw new ArgumentException("A speech-to-text deployment name must be provided, either directly or as a default in the connection settings.");
        }

        return ValueTask.FromResult(GetSpeechToTextClient(connection, deploymentName));
    }

    /// <summary>
    /// Gets provider name.
    /// </summary>
    protected abstract string GetProviderName();

    /// <summary>
    /// Gets chat client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected abstract IChatClient GetChatClient(AIProviderConnectionEntry connection, string deploymentName);

    /// <summary>
    /// Gets embedding generator.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected abstract IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator(AIProviderConnectionEntry connection, string deploymentName);
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    /// <summary>
    /// Gets image generator.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected abstract IImageGenerator GetImageGenerator(AIProviderConnectionEntry connection, string deploymentName);

    /// <summary>
    /// Gets speech to text client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected abstract ISpeechToTextClient GetSpeechToTextClient(AIProviderConnectionEntry connection, string deploymentName);
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    /// <summary>
    /// Gets text to speech client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    public virtual ValueTask<ITextToSpeechClient> GetTextToSpeechClientAsync(AIProviderConnectionEntry connection, string deploymentName = null)
    {
        throw new NotSupportedException($"The provider '{GetProviderName()}' does not support text-to-speech.");
    }
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    /// <summary>
    /// Gets speech voices.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="deploymentName">The deployment name.</param>
    public virtual Task<SpeechVoice[]> GetSpeechVoicesAsync(AIProviderConnectionEntry connection, string deploymentName = null)
    {
        return Task.FromResult(Array.Empty<SpeechVoice>());
    }
}
