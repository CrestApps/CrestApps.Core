using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;
using Microsoft.AspNetCore.DataProtection;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Opens the voice library of a deployment's provider through the registered <see cref="IVoiceLibraryProvider"/>s.
/// </summary>
public sealed class DefaultVoiceLibraryResolver : IVoiceLibraryResolver
{
    private readonly IEnumerable<IVoiceLibraryProvider> _libraryProviders;
    private readonly IEnumerable<IAIProviderConnectionHandler> _connectionHandlers;
    private readonly IAIProviderConnectionStore _connectionCatalog;
    private readonly IDataProtectionProvider _dataProtectionProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultVoiceLibraryResolver"/> class.
    /// </summary>
    /// <param name="libraryProviders">The registered voice library providers.</param>
    /// <param name="connectionHandlers">The connection handlers.</param>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    /// <param name="connectionCatalog">The connection store.</param>
    public DefaultVoiceLibraryResolver(
        IEnumerable<IVoiceLibraryProvider> libraryProviders,
        IEnumerable<IAIProviderConnectionHandler> connectionHandlers,
        IDataProtectionProvider dataProtectionProvider,
        IAIProviderConnectionStore connectionCatalog)
    {
        _libraryProviders = libraryProviders;
        _connectionHandlers = connectionHandlers;
        _dataProtectionProvider = dataProtectionProvider;
        _connectionCatalog = connectionCatalog;
    }

    /// <inheritdoc />
    public async Task<IVoiceLibrary> GetLibraryAsync(AIDeployment deployment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentException.ThrowIfNullOrEmpty(deployment.ClientName);

        var provider = _libraryProviders.FirstOrDefault(p => p.CanHandle(deployment.ClientName));

        if (provider is null)
        {
            return null;
        }

        return provider.GetLibrary(await GetConnectionEntryAsync(deployment));
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
