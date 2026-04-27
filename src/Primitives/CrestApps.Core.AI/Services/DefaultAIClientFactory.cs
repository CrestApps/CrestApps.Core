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
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentException.ThrowIfNullOrEmpty(deployment.ClientName);

        var connection = await GetConnectionEntryAsync(deployment);

        var client = await ResolveClientAsync(deployment, connection,
            (provider, conn, model) => provider.GetChatClientAsync(conn, model));

        return new AICompletionUsageTrackingChatClient(
                    client,
                    deployment.ClientName,
                    deployment.ConnectionName,
                    deployment.ModelName,
                    _serviceProvider,
                    _serviceProvider.GetRequiredService<ILogger<AICompletionUsageTrackingChatClient>>());
    }

    /// <summary>
    /// Creates embedding generator.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    public async ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(AIDeployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentException.ThrowIfNullOrEmpty(deployment.ClientName);

        var connection = await GetConnectionEntryAsync(deployment);

        return await ResolveClientAsync(deployment, connection,
                    (provider, conn, model) => provider.GetEmbeddingGeneratorAsync(conn, model));
    }

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    /// <summary>
    /// Creates image generator.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    public async ValueTask<IImageGenerator> CreateImageGeneratorAsync(AIDeployment deployment)
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentException.ThrowIfNullOrEmpty(deployment.ClientName);

        var connection = await GetConnectionEntryAsync(deployment);

        return await ResolveClientAsync(deployment, connection,
                    (provider, conn, model) => provider.GetImageGeneratorAsync(conn, model));
    }

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    /// <summary>
    /// Creates speech to text client.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    public async ValueTask<ISpeechToTextClient> CreateSpeechToTextClientAsync(AIDeployment deployment)
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentException.ThrowIfNullOrEmpty(deployment.ClientName);

        var connection = await GetConnectionEntryAsync(deployment);

        return await ResolveClientAsync(deployment, connection,
                    (provider, conn, model) => provider.GetSpeechToTextClientAsync(conn, model));
    }

#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    /// <summary>
    /// Creates text to speech client.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    public async ValueTask<ITextToSpeechClient> CreateTextToSpeechClientAsync(AIDeployment deployment)
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentException.ThrowIfNullOrEmpty(deployment.ClientName);

        var connection = await GetConnectionEntryAsync(deployment);

        return await ResolveClientAsync(deployment, connection,
                    (provider, conn, model) => provider.GetTextToSpeechClientAsync(conn, model));
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
}
