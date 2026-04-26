using CrestApps.Core.AI.Mcp.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Mcp;

/// <summary>
/// YesSql map index for <see cref="McpResource"/>, storing the item identifier,
/// display text, and source to support efficient MCP resource queries.
/// </summary>
public sealed class McpResourceIndex : CatalogItemIndex, ISourceAwareIndex
{
    /// <summary>
    /// Gets or sets the human-readable display text of the MCP resource.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the source or provider name of the MCP resource.
    /// </summary>
    public string Source { get; set; }
}

/// <summary>
/// YesSql index provider that maps <see cref="McpResource"/> documents
/// to <see cref="McpResourceIndex"/> entries in the AI collection.
/// </summary>
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
