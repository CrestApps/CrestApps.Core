using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Represents the AI Deployment Manager Base.
/// </summary>
public abstract class AIDeploymentManagerBase : NamedSourceCatalogManager<AIDeployment>, IAIDeploymentManager
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIDeploymentManagerBase"/> class.
    /// </summary>
    /// <param name="deploymentStore">The deployment store.</param>
    /// <param name="handlers">The handlers.</param>
    /// <param name="logger">The logger.</param>
    public AIDeploymentManagerBase(
        IAIDeploymentStore deploymentStore,
        IEnumerable<ICatalogEntryHandler<AIDeployment>> handlers,
        ILogger<AIDeploymentManagerBase> logger)
        : base(deploymentStore, handlers, logger)
    {
    }

    /// <summary>
    /// Gets all.
    /// </summary>
    /// <param name="clientName">The client name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IEnumerable<AIDeployment>> GetAllAsync(string clientName, CancellationToken cancellationToken = default)
    {
        var deployments = (await Catalog.GetAllAsync(cancellationToken))
            .Where(x => string.Equals(x.ClientName, clientName, StringComparison.OrdinalIgnoreCase));

        foreach (var deployment in deployments)
        {
            await LoadAsync(deployment, cancellationToken);
        }

        return deployments;
    }

    /// <summary>
    /// Gets by capability.
    /// </summary>
    /// <param name="capability">The capability.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IEnumerable<AIDeployment>> GetByCapabilityAsync(AIDeploymentCapability capability, CancellationToken cancellationToken = default)
    {
        var deployments = (await Catalog.GetAllAsync(cancellationToken))
            .Where(x => x.SupportsCapability(capability));

        foreach (var deployment in deployments)
        {
            await LoadAsync(deployment, cancellationToken);
        }

        return deployments;
    }

    /// <summary>
    /// Gets by legacy type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    [Obsolete("Use GetByCapabilityAsync instead.")]
    public ValueTask<IEnumerable<AIDeployment>> GetByTypeAsync(AIDeploymentType type, CancellationToken cancellationToken = default)
    {
        return GetByCapabilityAsync(type.ToCapability(), cancellationToken);
    }

    /// <summary>
    /// Gets default.
    /// </summary>
    /// <param name="clientName">The client name.</param>
    /// <param name="capability">The capability.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<AIDeployment> GetDefaultAsync(string clientName, AIDeploymentCapability capability, CancellationToken cancellationToken = default)
    {
        var deployments = await GetAllAsync(clientName, cancellationToken);

        var candidates = deployments.Where(d => d.SupportsCapability(capability));

        return candidates.FirstOrDefault();
    }

    /// <summary>
    /// Gets default for a legacy type.
    /// </summary>
    /// <param name="clientName">The client name.</param>
    /// <param name="type">The type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    [Obsolete("Use the capability overload instead.")]
    public ValueTask<AIDeployment> GetDefaultAsync(string clientName, AIDeploymentType type, CancellationToken cancellationToken = default)
    {
        return GetDefaultAsync(clientName, type.ToCapability(), cancellationToken);
    }

    /// <summary>
    /// Resolves or default.
    /// </summary>
    /// <param name="capability">The capability.</param>
    /// <param name="deploymentName">The deployment name.</param>
    /// <param name="clientName">The client name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<AIDeployment> ResolveOrDefaultAsync(AIDeploymentCapability capability, string deploymentName = null, string clientName = null, CancellationToken cancellationToken = default)
    {
        return ResolveByCapabilityAsync(capability, deploymentName, clientName, cancellationToken);
    }

    /// <summary>
    /// Resolves or default for a legacy type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="deploymentName">The deployment name.</param>
    /// <param name="clientName">The client name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    [Obsolete("Use the capability overload instead.")]
    public ValueTask<AIDeployment> ResolveOrDefaultAsync(AIDeploymentType type, string deploymentName = null, string clientName = null, CancellationToken cancellationToken = default)
    {
        return ResolveOrDefaultAsync(type.ToCapability(), deploymentName, clientName, cancellationToken);
    }

    /// <summary>
    /// Gets all by capability.
    /// </summary>
    /// <param name="capability">The capability.</param>
    /// <param name="clientName">The client name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IEnumerable<AIDeployment>> GetAllByCapabilityAsync(AIDeploymentCapability capability, string clientName = null, CancellationToken cancellationToken = default)
    {
        var allDeployments = await GetAllAsync(cancellationToken);

        var filtered = allDeployments.Where(d => d.SupportsCapability(capability));

        if (!string.IsNullOrEmpty(clientName))
        {
            filtered = filtered.Where(d => string.Equals(d.ClientName, clientName, StringComparison.OrdinalIgnoreCase));
        }

        return filtered;
    }

    /// <summary>
    /// Gets all by legacy type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="clientName">The client name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    [Obsolete("Use GetAllByCapabilityAsync instead.")]
    public ValueTask<IEnumerable<AIDeployment>> GetAllByTypeAsync(AIDeploymentType type, string clientName = null, CancellationToken cancellationToken = default)
    {
        return GetAllByCapabilityAsync(type.ToCapability(), clientName, cancellationToken);
    }

    private async ValueTask<AIDeployment> ResolveByCapabilityAsync(AIDeploymentCapability capability, string deploymentName, string clientName, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(deploymentName))
        {
            var deployment = await FindBySelectorAsync(deploymentName, cancellationToken);

            if (deployment != null)
            {
                return deployment;
            }
        }

        var globalDefaultId = await GetGlobalDefaultSelectorAsync(capability);

        if (!string.IsNullOrEmpty(globalDefaultId))
        {
            var deployment = await FindBySelectorAsync(globalDefaultId, cancellationToken);

            if (deployment != null)
            {
                return deployment;
            }
        }

        return await GetFirstMatchingDeploymentAsync(capability, clientName, cancellationToken);
    }

    private async ValueTask<AIDeployment> GetFirstMatchingDeploymentAsync(AIDeploymentCapability capability, string clientName, CancellationToken cancellationToken)
    {
        var deployments = await GetAllAsync(cancellationToken);

        return deployments.FirstOrDefault(deployment =>
                {
                    if (!deployment.SupportsCapability(capability))
                    {
                        return false;
                    }

                    if (!string.IsNullOrEmpty(clientName) &&
                        !string.Equals(deployment.ClientName, clientName, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    return true;
                });
    }

    private async ValueTask<AIDeployment> FindBySelectorAsync(string selector, CancellationToken cancellationToken)
    {
        var deployment = await FindByIdAsync(selector, cancellationToken);

        if (deployment != null)
        {
            return deployment;
        }

        return await FindByNameAsync(selector, cancellationToken);
    }

    private async ValueTask<string> GetGlobalDefaultSelectorAsync(AIDeploymentCapability capability)
    {
        var settings = await GetDefaultAIDeploymentSettingsAsync();

        return capability switch
        {
            AIDeploymentCapability.Chat => settings.DefaultChatDeploymentName,
            AIDeploymentCapability.Utility => settings.DefaultUtilityDeploymentName,
            AIDeploymentCapability.Embedding => settings.DefaultEmbeddingDeploymentName,
            AIDeploymentCapability.Image => settings.DefaultImageDeploymentName,
            AIDeploymentCapability.Vision => settings.DefaultVisionDeploymentName,
            AIDeploymentCapability.SpeechToText => settings.DefaultSpeechToTextDeploymentName,
            AIDeploymentCapability.TextToSpeech => settings.DefaultTextToSpeechDeploymentName,
            _ => null,
        };
    }

    /// <summary>
    /// Gets default ai deployment settings.
    /// </summary>
    protected abstract ValueTask<DefaultAIDeploymentSettings> GetDefaultAIDeploymentSettingsAsync();
}
