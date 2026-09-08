using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Realtime;
using CrestApps.Core.AI.Speech;
using Microsoft.AspNetCore.DataProtection;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Resolves the available real-time voices for a deployment by delegating to the matching
/// <see cref="IAIClientProvider"/>. Mirrors <see cref="DefaultSpeechVoiceResolver"/> for the realtime path.
/// </summary>
public sealed class DefaultRealtimeVoiceResolver : IRealtimeVoiceResolver
{
    private readonly IEnumerable<IAIClientProvider> _clientProviders;
    private readonly IEnumerable<IAIProviderConnectionHandler> _connectionHandlers;
    private readonly IAIProviderConnectionStore _connectionCatalog;
    private readonly IAIDeploymentStore _deploymentStore;
    private readonly IDataProtectionProvider _dataProtectionProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultRealtimeVoiceResolver"/> class.
    /// </summary>
    /// <param name="clientProviders">The client providers.</param>
    /// <param name="connectionHandlers">The connection handlers.</param>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    /// <param name="connectionCatalog">The connection catalog.</param>
    /// <param name="deploymentStore">The deployment store, used to reach the speaking deployment of a cascaded realtime deployment.</param>
    public DefaultRealtimeVoiceResolver(
        IEnumerable<IAIClientProvider> clientProviders,
        IEnumerable<IAIProviderConnectionHandler> connectionHandlers,
        IDataProtectionProvider dataProtectionProvider,
        IAIProviderConnectionStore connectionCatalog,
        IAIDeploymentStore deploymentStore)
    {
        _clientProviders = clientProviders;
        _connectionHandlers = connectionHandlers;
        _dataProtectionProvider = dataProtectionProvider;
        _connectionCatalog = connectionCatalog;
        _deploymentStore = deploymentStore;
    }

    /// <summary>
    /// Gets the available real-time voices for the specified deployment.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    public async Task<SpeechVoice[]> GetVoicesAsync(AIDeployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        // A cascaded deployment does not speak: the deployment it names for text-to-speech does, so the
        // voices to choose from are that deployment's speech voices.
        if (deployment.TryGet<CascadedRealtimeMetadata>(out var cascade) && cascade.IsComplete())
        {
            var speakingDeployment = await _deploymentStore.FindByNameAsync(cascade.TextToSpeechDeploymentName);

            if (speakingDeployment is null)
            {
                return [];
            }

            return await GetVoicesAsync(speakingDeployment, static (provider, connection, model) => provider.GetSpeechVoicesAsync(connection, model));
        }

        ArgumentException.ThrowIfNullOrEmpty(deployment.ClientName);

        return await GetVoicesAsync(deployment, static (provider, connection, model) => provider.GetRealtimeVoicesAsync(connection, model));
    }

    /// <summary>
    /// Reads the voices of a deployment from the first client provider that can handle it.
    /// </summary>
    /// <param name="deployment">The deployment whose voices are wanted.</param>
    /// <param name="read">Reads the voices from the resolved provider.</param>
    private async Task<SpeechVoice[]> GetVoicesAsync(
        AIDeployment deployment,
        Func<IAIClientProvider, AIProviderConnectionEntry, string, Task<SpeechVoice[]>> read)
    {
        ArgumentException.ThrowIfNullOrEmpty(deployment.ClientName);

        var connectionEntry = await GetConnectionEntryAsync(deployment);

        foreach (var clientProvider in _clientProviders)
        {
            if (!clientProvider.CanHandle(deployment.ClientName))
            {
                continue;
            }

            return await read(clientProvider, connectionEntry, deployment.ModelName);
        }

        return [];
    }

    private async ValueTask<AIProviderConnectionEntry> GetConnectionEntryAsync(AIDeployment deployment)
    {
        if (!string.IsNullOrEmpty(deployment.ConnectionName))
        {
            var connection = await _connectionCatalog.GetAsync(deployment.ConnectionName, deployment.ClientName);
            if (connection != null)
            {
                return AIProviderConnectionEntryFactory.Create(connection, _connectionHandlers);
            }

            throw new InvalidOperationException(
                $"Unable to find connection '{deployment.ConnectionName}' for provider '{deployment.ClientName}'.");
        }

        return AIDeploymentConnectionEntryFactory.Create(deployment, _dataProtectionProvider);
    }
}
