using CrestApps.Core.AI.Models;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

public sealed class AIProfileIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
{
    public string Name { get; set; }

    public string Source { get; set; }
}

public sealed class AIProfileIndexProvider : IndexProvider<AIProfile>
{
    internal AIProfileIndexProvider(string collectionName = null)
    {
        CollectionName = collectionName;
    }

    public override void Describe(DescribeContext<AIProfile> context)
    {
        context.For<AIProfileIndex>()
            .Map(profile => new AIProfileIndex
            {
                ItemId = profile.ItemId,
                Name = profile.Name,
                Source = profile.Source,
            });
    }
}
