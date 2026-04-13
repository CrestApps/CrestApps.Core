using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

public sealed class AIDeploymentIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
{
    public string Name { get; set; }

    public string Source { get; set; }
}

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
