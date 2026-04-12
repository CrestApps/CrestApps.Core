using CrestApps.Core.AI.Mcp.Models;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Mcp;

public sealed class McpResourceIndex : CatalogItemIndex, ISourceAwareIndex
{
    public string DisplayText { get; set; }

    public string Source { get; set; }
}

public sealed class McpResourceIndexProvider : IndexProvider<McpResource>
{
    internal McpResourceIndexProvider(string collectionName = null)
    {
        CollectionName = collectionName;
    }

    public override void Describe(DescribeContext<McpResource> context)
    {
        context.For<McpResourceIndex>()
            .Map(resource => new McpResourceIndex
            {
                ItemId = resource.ItemId,
                DisplayText = resource.DisplayText,
                Source = resource.Source,
            });
    }
}
