using CrestApps.Core.AI.A2A.Models;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.A2A;

public sealed class A2AConnectionIndex : CatalogItemIndex
{
    public string DisplayText { get; set; }
}

public sealed class A2AConnectionIndexProvider : IndexProvider<A2AConnection>
{
    internal A2AConnectionIndexProvider(string collectionName = null)
    {
        CollectionName = collectionName;
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
