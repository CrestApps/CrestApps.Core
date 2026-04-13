using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.DataSources;

public sealed class AIDataSourceIndex : CatalogItemIndex
{
    public string DisplayText { get; set; }

    public string SourceIndexProfileName { get; set; }
}

public sealed class AIDataSourceIndexProvider : IndexProvider<AIDataSource>
{
    public AIDataSourceIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AIDocsCollectionName;
    }

    public override void Describe(DescribeContext<AIDataSource> context)
    {
        context.For<AIDataSourceIndex>()
            .Map(ds => new AIDataSourceIndex
            {
                ItemId = ds.ItemId,
                DisplayText = ds.DisplayText,
                SourceIndexProfileName = ds.SourceIndexProfileName,
            });
    }
}
