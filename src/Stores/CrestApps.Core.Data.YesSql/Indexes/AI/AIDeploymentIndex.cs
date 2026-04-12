using CrestApps.Core.AI.Models;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

public sealed class AIDeploymentIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
{
    public string Name { get; set; }

    public string Source { get; set; }
}

public sealed class AIDeploymentIndexProvider : IndexProvider<AIDeployment>
{
    internal AIDeploymentIndexProvider(string collectionName = null)
    {
        CollectionName = collectionName;
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
