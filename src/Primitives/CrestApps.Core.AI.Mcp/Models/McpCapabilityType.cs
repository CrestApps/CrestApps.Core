namespace CrestApps.Core.AI.Mcp.Models;

/// <summary>
/// Represents the type of capability exposed by an MCP server.
/// </summary>
public enum McpCapabilityType
{
    /// <summary>
    /// The tool value.
    /// </summary>
    Tool,

    /// <summary>
    /// The prompt value.
    /// </summary>
    Prompt,

    /// <summary>
    /// The resource value.
    /// </summary>
    Resource,

    /// <summary>
    /// The resource Template value.
    /// </summary>
    ResourceTemplate,
}
