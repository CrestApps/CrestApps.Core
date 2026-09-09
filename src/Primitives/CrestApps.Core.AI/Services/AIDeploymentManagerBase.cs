using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Represents the AI Deployment Manager Base.
/// </summary>
public abstract class AIDeploymentManagerBase : NamedSourceCatalogManager<AIDeployment>, IAIDeploymentManager
{
    private readonly AIDeploymentSlotOptions _slotOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIDeploymentManagerBase"/> class using the deployment
    /// slots that ship with the framework.
    /// </summary>
    /// <param name="deploymentStore">The deployment store.</param>
    /// <param name="handlers">The handlers.</param>
    /// <param name="logger">The logger.</param>
    /// <remarks>
    /// A manager constructed this way does not see slots that modules registered through
    /// <c>AddAIDeploymentSlot</c>. Prefer the overload that takes the registered options.
    /// </remarks>
    public AIDeploymentManagerBase(
        IAIDeploymentStore deploymentStore,
        IEnumerable<ICatalogEntryHandler<AIDeployment>> handlers,
        ILogger<AIDeploymentManagerBase> logger)
        : this(deploymentStore, handlers, logger, slotOptions: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AIDeploymentManagerBase"/> class.
    /// </summary>
    /// <param name="deploymentStore">The deployment store.</param>
    /// <param name="handlers">The handlers.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="slotOptions">The registered deployment slots.</param>
    public AIDeploymentManagerBase(
        IAIDeploymentStore deploymentStore,
        IEnumerable<ICatalogEntryHandler<AIDeployment>> handlers,
        ILogger<AIDeploymentManagerBase> logger,
        IOptions<AIDeploymentSlotOptions> slotOptions)
        : base(deploymentStore, handlers, logger)
    {
        _slotOptions = slotOptions?.Value ?? AIDeploymentSlotOptions.CreateDefault();
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

    /// <inheritdoc/>
    public async ValueTask<AIDeployment> ResolveSlotAsync(
        string slotName,
        string deploymentName = null,
        string clientName = null,
        IReadOnlyDictionary<string, string> fallbackDeploymentNames = null,
        CancellationToken cancellationToken = default)
    {
        var slot = _slotOptions.Find(slotName);

        if (slot is null)
        {
            return null;
        }

        var settings = await GetDefaultAIDeploymentSettingsAsync();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var current = slot;
        var terminal = slot;

        while (current is not null && visited.Add(current.Name))
        {
            terminal = current;

            var explicitName = ReferenceEquals(current, slot)
                ? deploymentName
                : GetFallbackDeploymentName(fallbackDeploymentNames, current.Name);

            var resolved = await FindQualifiedAsync(explicitName, current, cancellationToken);

            if (resolved is not null)
            {
                return resolved;
            }

            resolved = await FindQualifiedAsync(current.GetDefaultDeploymentName?.Invoke(settings), current, cancellationToken);

            if (resolved is not null)
            {
                return resolved;
            }

            current = _slotOptions.Find(current.FallbackSlotName);
        }

        // Evaluated exactly once, at the very end of the chain. Evaluating it per link would let the first
        // slot in the chain answer with an arbitrary capable deployment and make every later link dead code.
        return await GetFirstQualifiedDeploymentAsync(terminal, clientName, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<IEnumerable<AIDeployment>> GetAllBySlotAsync(string slotName, string clientName = null, CancellationToken cancellationToken = default)
    {
        var slot = _slotOptions.Find(slotName);

        if (slot is null)
        {
            return [];
        }

        var allDeployments = await GetAllAsync(cancellationToken);

        var filtered = allDeployments.Where(d => QualifiesForSlot(d, slot));

        if (!string.IsNullOrEmpty(clientName))
        {
            filtered = filtered.Where(d => string.Equals(d.ClientName, clientName, StringComparison.OrdinalIgnoreCase));
        }

        return filtered;
    }

    /// <summary>
    /// Determines whether a deployment declares the capability the slot requires.
    /// </summary>
    private static bool QualifiesForSlot(AIDeployment deployment, AIDeploymentSlotDescriptor slot)
    {
        if (deployment is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(slot.RequiredFeature) && string.IsNullOrWhiteSpace(slot.ExcludedFeature))
        {
            return true;
        }

        // A deployment that declares no capability metadata at all is unconstrained. That is what keeps
        // textGeneration opt-out; every other feature has to be declared.
        if (!deployment.TryGet<AIDeploymentMetadata>(out var metadata))
        {
            return slot.AllowUnconstrained;
        }

        // Checked before the required feature, because it overrules it: a deployment that declares both
        // realtime and text generation still cannot serve a text completion.
        if (!string.IsNullOrWhiteSpace(slot.ExcludedFeature) && metadata.SupportsFeature(slot.ExcludedFeature))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(slot.RequiredFeature) || metadata.SupportsFeature(slot.RequiredFeature);
    }

    private async ValueTask<AIDeployment> FindQualifiedAsync(string selector, AIDeploymentSlotDescriptor slot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(selector))
        {
            return null;
        }

        var deployment = await FindBySelectorAsync(selector, cancellationToken);

        if (deployment is null)
        {
            return null;
        }

        return QualifiesForSlot(deployment, slot)
            ? deployment
            : null;
    }

    private async ValueTask<AIDeployment> GetFirstQualifiedDeploymentAsync(AIDeploymentSlotDescriptor slot, string clientName, CancellationToken cancellationToken)
    {
        var deployments = await GetAllAsync(cancellationToken);

        return deployments.FirstOrDefault(deployment =>
        {
            if (!QualifiesForSlot(deployment, slot))
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

    private static string GetFallbackDeploymentName(IReadOnlyDictionary<string, string> fallbackDeploymentNames, string slotName)
    {
        return fallbackDeploymentNames is not null && fallbackDeploymentNames.TryGetValue(slotName, out var name)
            ? name
            : null;
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

    /// <summary>
    /// Gets default ai deployment settings.
    /// </summary>
    protected abstract ValueTask<DefaultAIDeploymentSettings> GetDefaultAIDeploymentSettingsAsync();
}
