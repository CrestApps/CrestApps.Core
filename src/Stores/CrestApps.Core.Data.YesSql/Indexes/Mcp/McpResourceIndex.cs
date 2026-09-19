using CrestApps.Core.AI.Mcp.Models;

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
