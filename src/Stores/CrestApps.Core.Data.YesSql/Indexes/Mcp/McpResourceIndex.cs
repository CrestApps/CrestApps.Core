using CrestApps.Core.AI.Mcp.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Mcp;

public sealed class McpResourceIndex : CatalogItemIndex, ISourceAwareIndex
{
    public string DisplayText { get; set; }

    public string Source { get; set; }
}

public sealed class McpResourceIndexProvider : IndexProvider<McpResource>
{
    public McpResourceIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
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
