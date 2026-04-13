using CrestApps.Core.AI.A2A.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.A2A;

public sealed class A2AConnectionIndex : CatalogItemIndex
{
    public string DisplayText { get; set; }
}

public sealed class A2AConnectionIndexProvider : IndexProvider<A2AConnection>
{
    public A2AConnectionIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    public override void Describe(DescribeContext<A2AConnection> context)
    {
        context.For<A2AConnectionIndex>()
            .Map(connection => new A2AConnectionIndex
            {
                ItemId = connection.ItemId,
                DisplayText = connection.DisplayText,
            });
    }
}
