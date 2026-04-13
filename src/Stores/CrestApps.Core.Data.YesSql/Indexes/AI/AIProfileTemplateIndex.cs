using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

public sealed class AIProfileTemplateIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
{
    public string Name { get; set; }

    public string Source { get; set; }
}

public sealed class AIProfileTemplateIndexProvider : IndexProvider<AIProfileTemplate>
{
    public AIProfileTemplateIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    public override void Describe(DescribeContext<AIProfileTemplate> context)
    {
        context.For<AIProfileTemplateIndex>()
            .Map(template => new AIProfileTemplateIndex
            {
                ItemId = template.ItemId,
                Name = template.Name,
                Source = template.Source,
            });
    }
}
