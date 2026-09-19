using CrestApps.Core.AI.Mcp.Models;

namespace CrestApps.Core.AI.Mcp;

/// <summary>
/// Represents a single MCP capability with its pre-computed embedding vector.
/// </summary>
public sealed class McpCapabilityEmbeddingEntry
{
    /// <summary>
    /// Gets or sets the MCP connection identifier.
    /// </summary>
    public required string ConnectionId { get; init; }

    /// <summary>
    /// Gets or sets the display name of the MCP connection.
    /// </summary>
    public required string ConnectionDisplayText { get; init; }

    /// <summary>
    /// Gets or sets the capability name.
    /// </summary>
    public required string CapabilityName { get; init; }

    /// <summary>
    /// Gets or sets the capability description.
    /// </summary>
    public required string CapabilityDescription { get; init; }

    /// <summary>
    /// Gets or sets the type of capability.
    /// </summary>
    public required McpCapabilityType CapabilityType { get; init; }

    /// <summary>
    /// Gets or sets the embedding vector for this capability's text representation.
    /// </summary>
    public required float[] Embedding { get; init; }
}
