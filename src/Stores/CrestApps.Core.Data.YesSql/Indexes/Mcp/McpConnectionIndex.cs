using CrestApps.Core.AI.Mcp.Models;

namespace CrestApps.Core.Data.YesSql.Indexes.Mcp;

/// <summary>
/// YesSql map index for <see cref="McpConnection"/>, storing the item identifier,
/// display text, and source to support efficient MCP connection queries.
/// </summary>
public sealed class McpConnectionIndex : CatalogItemIndex, ISourceAwareIndex
{
    /// <summary>
    /// Gets or sets the human-readable display text of the MCP connection.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the source or provider name of the MCP connection.
    /// </summary>
    public string Source { get; set; }
}
