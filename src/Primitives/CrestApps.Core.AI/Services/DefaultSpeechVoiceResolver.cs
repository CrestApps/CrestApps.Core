using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.DataProtection;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Represents the default Speech Voice Resolver.
/// </summary>
public sealed class DefaultSpeechVoiceResolver : ISpeechVoiceResolver
{
    private readonly IEnumerable<IAIClientProvider> _clientProviders;
    private readonly IEnumerable<IAIProviderConnectionHandler> _connectionHandlers;
    private readonly INamedSourceCatalog<AIProviderConnection> _connectionCatalog;
    private readonly IDataProtectionProvider _dataProtectionProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultSpeechVoiceResolver"/> class.
    /// </summary>
    /// <param name="clientProviders">The client providers.</param>
    /// <param name="connectionHandlers">The connection handlers.</param>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    /// <param name="connectionCatalog">The connection catalog.</param>
    public DefaultSpeechVoiceResolver(
        IEnumerable<IAIClientProvider> clientProviders,
        IEnumerable<IAIProviderConnectionHandler> connectionHandlers,
        IDataProtectionProvider dataProtectionProvider,
        INamedSourceCatalog<AIProviderConnection> connectionCatalog)
    {
        _clientProviders = clientProviders;
        _connectionHandlers = connectionHandlers;
        _dataProtectionProvider = dataProtectionProvider;
        _connectionCatalog = connectionCatalog;
    }

    /// <summary>
    /// Gets speech voices.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    public async Task<SpeechVoice[]> GetSpeechVoicesAsync(AIDeployment deployment)
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

            return await clientProvider.GetSpeechVoicesAsync(connectionEntry, deployment.ModelName);
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
