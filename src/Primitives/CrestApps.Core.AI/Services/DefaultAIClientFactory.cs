using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Represents the default AI Client Factory.
/// </summary>
public sealed class DefaultAIClientFactory : IAIClientFactory
{
    private readonly IAIProviderConnectionStore _connectionCatalog;
    private readonly IEnumerable<IAIClientProvider> _clientProviders;
    private readonly IEnumerable<IAIProviderConnectionHandler> _connectionHandlers;

    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIClientFactory"/> class.
    /// </summary>
    /// <param name="clientProviders">The client providers.</param>
    /// <param name="connectionHandlers">The connection handlers.</param>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="connectionCatalog">The connection catalog.</param>
    public DefaultAIClientFactory(
        IEnumerable<IAIClientProvider> clientProviders,
        IEnumerable<IAIProviderConnectionHandler> connectionHandlers,
        IDataProtectionProvider dataProtectionProvider,
        IServiceProvider serviceProvider,
        IAIProviderConnectionStore connectionCatalog)
    {
        _connectionCatalog = connectionCatalog;
        _clientProviders = clientProviders;
        _connectionHandlers = connectionHandlers;
        _dataProtectionProvider = dataProtectionProvider;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Creates chat client.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    public async ValueTask<IChatClient> CreateChatClientAsync(AIDeployment deployment)
    {
        return await CreateChatClientAsync(deployment, null);
    }

    /// <summary>
    /// Creates chat client and applies optional pipeline configuration before building the final client.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    /// <param name="configurePipeline">The optional pipeline configuration.</param>
    public async ValueTask<IChatClient> CreateChatClientAsync(AIDeployment deployment, Action<ChatClientBuilder> configurePipeline)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentException.ThrowIfNullOrEmpty(deployment.ClientName);

        var connection = await GetConnectionEntryAsync(deployment);

        var client = await ResolveClientAsync(deployment, connection,
            (provider, conn, model) => provider.GetChatClientAsync(conn, model));

        client = new AICompletionUsageTrackingChatClient(
            client,
            deployment.ClientName,
            deployment.ConnectionName,
            deployment.ModelName,
            _serviceProvider,
            _serviceProvider.GetRequiredService<ILogger<AICompletionUsageTrackingChatClient>>());

        return BuildChatClient(client, configurePipeline);
    }

    /// <summary>
    /// Creates embedding generator.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    public async ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(AIDeployment deployment)
    {
        return await CreateEmbeddingGeneratorAsync(deployment, null);
    }

    /// <summary>
    /// Creates embedding generator and applies optional pipeline configuration before building the final generator.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    /// <param name="configurePipeline">The optional pipeline configuration.</param>
    public async ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(
        AIDeployment deployment,
        Action<EmbeddingGeneratorBuilder<string, Embedding<float>>> configurePipeline)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentException.ThrowIfNullOrEmpty(deployment.ClientName);

        var connection = await GetConnectionEntryAsync(deployment);

        var generator = await ResolveClientAsync(deployment, connection,
            (provider, conn, model) => provider.GetEmbeddingGeneratorAsync(conn, model));

        return BuildEmbeddingGenerator(generator, configurePipeline);
    }

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    /// <summary>
    /// Creates image generator.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    public async ValueTask<IImageGenerator> CreateImageGeneratorAsync(AIDeployment deployment)
    {
        return await CreateImageGeneratorAsync(deployment, null);
    }

    /// <summary>
    /// Creates image generator and applies optional pipeline configuration before building the final generator.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    /// <param name="configurePipeline">The optional pipeline configuration.</param>
    public async ValueTask<IImageGenerator> CreateImageGeneratorAsync(AIDeployment deployment, Action<ImageGeneratorBuilder> configurePipeline)
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentException.ThrowIfNullOrEmpty(deployment.ClientName);

        var connection = await GetConnectionEntryAsync(deployment);

        var generator = await ResolveClientAsync(deployment, connection,
            (provider, conn, model) => provider.GetImageGeneratorAsync(conn, model));

        return BuildImageGenerator(generator, configurePipeline);
    }

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    /// <summary>
    /// Creates speech to text client.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    public async ValueTask<ISpeechToTextClient> CreateSpeechToTextClientAsync(AIDeployment deployment)
    {
        return await CreateSpeechToTextClientAsync(deployment, null);
    }

    /// <summary>
    /// Creates speech to text client and applies optional pipeline configuration before building the final client.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    /// <param name="configurePipeline">The optional pipeline configuration.</param>
    public async ValueTask<ISpeechToTextClient> CreateSpeechToTextClientAsync(AIDeployment deployment, Action<SpeechToTextClientBuilder> configurePipeline)
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentException.ThrowIfNullOrEmpty(deployment.ClientName);

        var connection = await GetConnectionEntryAsync(deployment);

        var client = await ResolveClientAsync(deployment, connection,
            (provider, conn, model) => provider.GetSpeechToTextClientAsync(conn, model));

        return BuildSpeechToTextClient(client, configurePipeline);
    }

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    /// <summary>
    /// Creates text to speech client.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    public async ValueTask<ITextToSpeechClient> CreateTextToSpeechClientAsync(AIDeployment deployment)
    {
        return await CreateTextToSpeechClientAsync(deployment, null);
    }

    /// <summary>
    /// Creates text to speech client and applies optional pipeline configuration before building the final client.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    /// <param name="configurePipeline">The optional pipeline configuration.</param>
    public async ValueTask<ITextToSpeechClient> CreateTextToSpeechClientAsync(AIDeployment deployment, Action<TextToSpeechClientBuilder> configurePipeline)
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentException.ThrowIfNullOrEmpty(deployment.ClientName);

        var connection = await GetConnectionEntryAsync(deployment);

        var client = await ResolveClientAsync(deployment, connection,
            (provider, conn, model) => provider.GetTextToSpeechClientAsync(conn, model));

        return BuildTextToSpeechClient(client, configurePipeline);
    }

    /// <summary>
    /// Resolves a client from the first registered provider that can handle the deployment's client name.
    /// </summary>
    private async ValueTask<TResult> ResolveClientAsync<TResult>(
        AIDeployment deployment,
        AIProviderConnectionEntry connection,
        Func<IAIClientProvider, AIProviderConnectionEntry, string, ValueTask<TResult>> factory)
    {
        foreach (var clientProvider in _clientProviders)
        {
            if (!clientProvider.CanHandle(deployment.ClientName))
            {
                continue;
            }

            return await factory(clientProvider, connection, deployment.ModelName);
        }

        throw new ArgumentException($"Unable to find an implementation of '{nameof(IAIClientProvider)}' that can handle the client '{deployment.ClientName}'.");
    }

    private async ValueTask<AIProviderConnectionEntry> GetConnectionEntryAsync(AIDeployment deployment)
    {
        if (!string.IsNullOrEmpty(deployment.ConnectionName))
        {
            var connection = await _connectionCatalog.GetAsync(deployment.ConnectionName, deployment.ClientName);

            if (connection == null)
            {
                throw new ArgumentException($"Connection '{deployment.ConnectionName}' not found within the client '{deployment.ClientName}'.");
            }

            return AIProviderConnectionEntryFactory.Create(connection, _connectionHandlers);
        }

        // Contained-connection deployment: build an AIProviderConnectionEntry from the deployment's Properties.

        return AIDeploymentConnectionEntryFactory.Create(deployment, _dataProtectionProvider);
    }

    private IChatClient BuildChatClient(IChatClient client, Action<ChatClientBuilder> configurePipeline)
    {
        if (configurePipeline is null)
        {
            return client;
        }

        var builder = client.AsBuilder();
        configurePipeline.Invoke(builder);

        return builder.Build(_serviceProvider);
    }

    private IEmbeddingGenerator<string, Embedding<float>> BuildEmbeddingGenerator(IEmbeddingGenerator<string, Embedding<float>> generator, Action<EmbeddingGeneratorBuilder<string, Embedding<float>>> configurePipeline)
    {
        if (configurePipeline is null)
        {
            return generator;
        }

        var builder = generator.AsBuilder();
        configurePipeline.Invoke(builder);

        return builder.Build(_serviceProvider);
    }

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    private IImageGenerator BuildImageGenerator(IImageGenerator generator, Action<ImageGeneratorBuilder> configurePipeline)
    {
        if (configurePipeline is null)
        {
            return generator;
        }

        var builder = generator.AsBuilder();
        configurePipeline.Invoke(builder);

        return builder.Build(_serviceProvider);
    }

    private ISpeechToTextClient BuildSpeechToTextClient(ISpeechToTextClient client, Action<SpeechToTextClientBuilder> configurePipeline)
    {
        if (configurePipeline is null)
        {
            return client;
        }

        var builder = client.AsBuilder();
        configurePipeline.Invoke(builder);

        return builder.Build(_serviceProvider);
    }

    private ITextToSpeechClient BuildTextToSpeechClient(ITextToSpeechClient client, Action<TextToSpeechClientBuilder> configurePipeline)
    {
        if (configurePipeline is null)
        {
            return client;
        }

        var builder = client.AsBuilder();
        configurePipeline.Invoke(builder);

        return builder.Build(_serviceProvider);
    }
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
}
