using CrestApps.Core.AI.Mcp.Models;

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
