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
    /// Gets by type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IEnumerable<AIDeployment>> GetByTypeAsync(AIDeploymentType type, CancellationToken cancellationToken = default)
    {
        var deployments = (await Catalog.GetAllAsync(cancellationToken))
            .Where(x => x.SupportsType(type));

        foreach (var deployment in deployments)
        {
            await LoadAsync(deployment, cancellationToken);
        }

        return deployments;
    }

    /// <summary>
    /// Gets default.
    /// </summary>
    /// <param name="clientName">The client name.</param>
    /// <param name="type">The type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<AIDeployment> GetDefaultAsync(string clientName, AIDeploymentType type, CancellationToken cancellationToken = default)
    {
        var deployments = await GetAllAsync(clientName, cancellationToken);

        var candidates = deployments.Where(d => d.SupportsType(type));

return candidates.FirstOrDefault();
    }

    /// <summary>
    /// Resolves or default.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="deploymentName">The deployment name.</param>
    /// <param name="clientName">The client name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<AIDeployment> ResolveOrDefaultAsync(AIDeploymentType type, string deploymentName = null, string clientName = null, CancellationToken cancellationToken = default)
    {
        return ResolveByTypeAsync(type, deploymentName, clientName, cancellationToken);
    }

    /// <summary>
    /// Gets all by type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="clientName">The client name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IEnumerable<AIDeployment>> GetAllByTypeAsync(AIDeploymentType type, string clientName = null, CancellationToken cancellationToken = default)
    {
        var allDeployments = await GetAllAsync(cancellationToken);

        var filtered = allDeployments.Where(d => d.SupportsType(type));

        if (!string.IsNullOrEmpty(clientName))
        {
            filtered = filtered.Where(d => string.Equals(d.ClientName, clientName, StringComparison.OrdinalIgnoreCase));
        }

        return filtered;
    }

    private async ValueTask<AIDeployment> ResolveByTypeAsync(AIDeploymentType type, string deploymentName, string clientName, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(deploymentName))
        {
            var deployment = await FindBySelectorAsync(deploymentName, cancellationToken);

            if (deployment != null)
            {
                return deployment;
            }
        }

        var globalDefaultId = await GetGlobalDefaultSelectorAsync(type);

        if (!string.IsNullOrEmpty(globalDefaultId))
        {
            var deployment = await FindBySelectorAsync(globalDefaultId, cancellationToken);

            if (deployment != null)
            {
                return deployment;
            }
        }

        return await GetFirstMatchingDeploymentAsync(type, clientName, cancellationToken);
    }

    private async ValueTask<AIDeployment> GetFirstMatchingDeploymentAsync(AIDeploymentType type, string clientName, CancellationToken cancellationToken)
    {
        var deployments = await GetAllAsync(cancellationToken);

return deployments.FirstOrDefault(deployment =>
        {
            if (!deployment.SupportsType(type))
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

    private async ValueTask<string> GetGlobalDefaultSelectorAsync(AIDeploymentType type)
    {
        var settings = await GetDefaultAIDeploymentSettingsAsync();

return type switch
        {
            AIDeploymentType.Chat => settings.DefaultChatDeploymentName,
            AIDeploymentType.Utility => settings.DefaultUtilityDeploymentName,
            AIDeploymentType.Embedding => settings.DefaultEmbeddingDeploymentName,
            AIDeploymentType.Image => settings.DefaultImageDeploymentName,
            AIDeploymentType.SpeechToText => settings.DefaultSpeechToTextDeploymentName,
            AIDeploymentType.TextToSpeech => settings.DefaultTextToSpeechDeploymentName,
            _ => null,
        };
    }

    /// <summary>
    /// Gets default ai deployment settings.
    /// </summary>
    protected abstract ValueTask<DefaultAIDeploymentSettings> GetDefaultAIDeploymentSettingsAsync();
}
