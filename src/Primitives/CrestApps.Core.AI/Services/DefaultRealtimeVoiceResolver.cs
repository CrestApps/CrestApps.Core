using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.Models;
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
    private readonly IDataProtectionProvider _dataProtectionProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultRealtimeVoiceResolver"/> class.
    /// </summary>
    /// <param name="clientProviders">The client providers.</param>
    /// <param name="connectionHandlers">The connection handlers.</param>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    /// <param name="connectionCatalog">The connection catalog.</param>
    public DefaultRealtimeVoiceResolver(
        IEnumerable<IAIClientProvider> clientProviders,
        IEnumerable<IAIProviderConnectionHandler> connectionHandlers,
        IDataProtectionProvider dataProtectionProvider,
        IAIProviderConnectionStore connectionCatalog)
    {
        _clientProviders = clientProviders;
        _connectionHandlers = connectionHandlers;
        _dataProtectionProvider = dataProtectionProvider;
        _connectionCatalog = connectionCatalog;
    }

    /// <summary>
    /// Gets the available real-time voices for the specified deployment.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    public async Task<SpeechVoice[]> GetVoicesAsync(AIDeployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentException.ThrowIfNullOrEmpty(deployment.ClientName);

        var connectionEntry = await GetConnectionEntryAsync(deployment);

        foreach (var clientProvider in _clientProviders)
        {
            if (!clientProvider.CanHandle(deployment.ClientName))
            {
                continue;
            }

            return await clientProvider.GetRealtimeVoicesAsync(connectionEntry, deployment.ModelName);
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
