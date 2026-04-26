using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

/// <summary>
/// YesSql map index for <see cref="AIDeployment"/>, storing the item identifier,
/// unique name, and source to support efficient catalog queries.
/// </summary>
public sealed class AIDeploymentIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
{
    /// <summary>
    /// Gets or sets the unique technical name of the AI deployment.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the source or provider name of the AI deployment.
    /// </summary>
    public string Source { get; set; }
}

/// <summary>
/// YesSql index provider that maps <see cref="AIDeployment"/> documents
/// to <see cref="AIDeploymentIndex"/> entries in the AI collection.
/// </summary>
public sealed class AIDeploymentIndexProvider : IndexProvider<AIDeployment>
{
    public AIDeploymentIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    public override void Describe(DescribeContext<AIDeployment> context)
    {
        context.For<AIDeploymentIndex>()
            .Map(deployment => new AIDeploymentIndex
            {
                ItemId = deployment.ItemId,
                Name = deployment.Name,
                Source = deployment.Source,
            });
    }
}
