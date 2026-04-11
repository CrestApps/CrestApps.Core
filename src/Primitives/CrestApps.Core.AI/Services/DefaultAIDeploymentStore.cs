using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// The default multi-source AI deployment store. Aggregates entries from all
/// registered <see cref="INamedSourceCatalogSource{T}"/> implementations
/// (configuration, YesSql, EntityCore, or custom sources).
/// </summary>
public sealed class DefaultAIDeploymentStore : MultiSourceNamedSourceCatalog<AIDeployment>, IAIDeploymentStore
{
    public DefaultAIDeploymentStore(IEnumerable<INamedSourceCatalogSource<AIDeployment>> sources)
        : base(sources)
    {
    }

    protected override string GetItemId(AIDeployment entry) => entry.ItemId;
}
