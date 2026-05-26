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
    /// Gets by purpose.
    /// </summary>
    /// <param name="purpose">The purpose.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IEnumerable<AIDeployment>> GetByPurposeAsync(AIDeploymentPurpose purpose, CancellationToken cancellationToken = default)
    {
        var deployments = (await Catalog.GetAllAsync(cancellationToken))
            .Where(x => x.SupportsPurpose(purpose));

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
    [Obsolete("Use GetByPurposeAsync instead.")]
    public ValueTask<IEnumerable<AIDeployment>> GetByTypeAsync(AIDeploymentType type, CancellationToken cancellationToken = default)
    {
        return GetByPurposeAsync(type.ToPurpose(), cancellationToken);
    }

    /// <summary>
    /// Gets default.
    /// </summary>
    /// <param name="clientName">The client name.</param>
    /// <param name="purpose">The purpose.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<AIDeployment> GetDefaultAsync(string clientName, AIDeploymentPurpose purpose, CancellationToken cancellationToken = default)
    {
        var deployments = await GetAllAsync(clientName, cancellationToken);

        var candidates = deployments.Where(d => d.SupportsPurpose(purpose));

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
        return GetDefaultAsync(clientName, type.ToPurpose(), cancellationToken);
    }

    /// <summary>
    /// Resolves or default.
    /// </summary>
    /// <param name="purpose">The purpose.</param>
    /// <param name="deploymentName">The deployment name.</param>
    /// <param name="clientName">The client name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<AIDeployment> ResolveOrDefaultAsync(AIDeploymentPurpose purpose, string deploymentName = null, string clientName = null, CancellationToken cancellationToken = default)
    {
        return ResolveByPurposeAsync(purpose, deploymentName, clientName, cancellationToken);
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
        return ResolveOrDefaultAsync(type.ToPurpose(), deploymentName, clientName, cancellationToken);
    }

    /// <summary>
    /// Gets all by purpose.
    /// </summary>
    /// <param name="purpose">The purpose.</param>
    /// <param name="clientName">The client name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IEnumerable<AIDeployment>> GetAllByPurposeAsync(AIDeploymentPurpose purpose, string clientName = null, CancellationToken cancellationToken = default)
    {
        var allDeployments = await GetAllAsync(cancellationToken);

        var filtered = allDeployments.Where(d => d.SupportsPurpose(purpose));

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
    [Obsolete("Use GetAllByPurposeAsync instead.")]
    public ValueTask<IEnumerable<AIDeployment>> GetAllByTypeAsync(AIDeploymentType type, string clientName = null, CancellationToken cancellationToken = default)
    {
        return GetAllByPurposeAsync(type.ToPurpose(), clientName, cancellationToken);
    }

    private async ValueTask<AIDeployment> ResolveByPurposeAsync(AIDeploymentPurpose purpose, string deploymentName, string clientName, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(deploymentName))
        {
            var deployment = await FindBySelectorAsync(deploymentName, cancellationToken);

            if (deployment != null)
            {
                return deployment;
            }
        }

        var globalDefaultId = await GetGlobalDefaultSelectorAsync(purpose);

        if (!string.IsNullOrEmpty(globalDefaultId))
        {
            var deployment = await FindBySelectorAsync(globalDefaultId, cancellationToken);

            if (deployment != null)
            {
                return deployment;
            }
        }

        return await GetFirstMatchingDeploymentAsync(purpose, clientName, cancellationToken);
    }

    private async ValueTask<AIDeployment> GetFirstMatchingDeploymentAsync(AIDeploymentPurpose purpose, string clientName, CancellationToken cancellationToken)
    {
        var deployments = await GetAllAsync(cancellationToken);

        return deployments.FirstOrDefault(deployment =>
                {
                    if (!deployment.SupportsPurpose(purpose))
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

    private async ValueTask<string> GetGlobalDefaultSelectorAsync(AIDeploymentPurpose purpose)
    {
        var settings = await GetDefaultAIDeploymentSettingsAsync();

        return purpose switch
        {
            AIDeploymentPurpose.Chat => settings.DefaultChatDeploymentName,
            AIDeploymentPurpose.Utility => settings.DefaultUtilityDeploymentName,
            AIDeploymentPurpose.Embedding => settings.DefaultEmbeddingDeploymentName,
            AIDeploymentPurpose.Image => settings.DefaultImageDeploymentName,
            AIDeploymentPurpose.Vision => settings.DefaultVisionDeploymentName,
            AIDeploymentPurpose.SpeechToText => settings.DefaultSpeechToTextDeploymentName,
            AIDeploymentPurpose.TextToSpeech => settings.DefaultTextToSpeechDeploymentName,
            _ => null,
        };
    }

    /// <summary>
    /// Gets default ai deployment settings.
    /// </summary>
    protected abstract ValueTask<DefaultAIDeploymentSettings> GetDefaultAIDeploymentSettingsAsync();
}
