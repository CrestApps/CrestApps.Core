using CrestApps.Core.AI.Mcp.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Mcp;

/// <summary>
/// YesSql index provider that maps <see cref="McpResource"/> documents
/// to <see cref="McpResourceIndex"/> entries in the AI collection.
/// </summary>
public sealed class McpResourceIndexProvider : IndexProvider<McpResource>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpResourceIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public McpResourceIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the operation.
    /// </summary>
    /// <param name="context">The context.</param>
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
