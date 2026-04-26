using CrestApps.Core.AI.Mcp.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Mcp;

/// <summary>
/// YesSql map index for <see cref="McpPrompt"/>, storing the item identifier
/// and unique name to support efficient MCP prompt catalog queries.
/// </summary>
public sealed class McpPromptIndex : CatalogItemIndex, INameAwareIndex
{
    /// <summary>
    /// Gets or sets the unique technical name of the MCP prompt.
    /// </summary>
    public string Name { get; set; }
}

/// <summary>
/// YesSql index provider that maps <see cref="McpPrompt"/> documents
/// to <see cref="McpPromptIndex"/> entries in the AI collection.
/// </summary>
public sealed class McpPromptIndexProvider : IndexProvider<McpPrompt>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpPromptIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public McpPromptIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the operation.
    /// </summary>
    /// <param name="context">The context.</param>
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
