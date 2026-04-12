using CrestApps.Core.AI.Mcp.Models;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Mcp;

public sealed class McpConnectionIndex : CatalogItemIndex, ISourceAwareIndex
{
    public string DisplayText { get; set; }

    public string Source { get; set; }
}

public sealed class McpConnectionIndexProvider : IndexProvider<McpConnection>
{
    internal McpConnectionIndexProvider(string collectionName = null)
    {
        CollectionName = collectionName;
    }

    public override void Describe(DescribeContext<McpConnection> context)
    {
        context.For<McpConnectionIndex>()
            .Map(connection => new McpConnectionIndex
            {
                ItemId = connection.ItemId,
                DisplayText = connection.DisplayText,
                Source = connection.Source,
            });
    }
}
