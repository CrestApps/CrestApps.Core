using CrestApps.Core.AI.Mcp.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Mcp;

/// <summary>
/// YesSql index provider that maps <see cref="McpConnection"/> documents
/// to <see cref="McpConnectionIndex"/> entries in the AI collection.
/// </summary>
public sealed class McpConnectionIndexProvider : IndexProvider<McpConnection>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpConnectionIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public McpConnectionIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the operation.
    /// </summary>
    /// <param name="context">The context.</param>
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
