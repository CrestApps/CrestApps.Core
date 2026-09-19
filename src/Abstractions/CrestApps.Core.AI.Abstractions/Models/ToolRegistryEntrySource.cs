namespace CrestApps.Core.AI.Models;

/// <summary>
/// Identifies the origin of a tool registry entry.
/// </summary>
public enum ToolRegistryEntrySource
{
    /// <summary>
    /// A locally registered AI tool.
    /// </summary>
    Local,

    /// <summary>
    /// A tool provided by an MCP server connection.
    /// </summary>
    McpServer,

    /// <summary>
    /// A system tool automatically included by the orchestrator based on context availability.
    /// </summary>
    System,

    /// <summary>
    /// An AI agent profile exposed as a tool for multi-agent orchestration.
    /// </summary>
    Agent,

    /// <summary>
    /// A remote agent accessible via the Agent-to-Agent (A2A) protocol.
    /// </summary>
    A2AAgent,
}
