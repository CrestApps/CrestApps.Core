using CrestApps.Core.AI.Mcp.Models;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Mcp;

public sealed class McpPromptIndex : CatalogItemIndex, INameAwareIndex
{
    public string Name { get; set; }
}

public sealed class McpPromptIndexProvider : IndexProvider<McpPrompt>
{
    public override void Describe(DescribeContext<McpPrompt> context)
    {
        context.For<McpPromptIndex>()
            .Map(prompt => new McpPromptIndex
            {
                ItemId = prompt.ItemId,
                Name = prompt.Name,
            });
    }
}
