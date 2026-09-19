namespace CrestApps.Core.Mvc.Web.Areas.Admin.ViewModels;

/// <summary>
/// An AI agent that may be exposed to MCP clients as a callable tool.
/// </summary>
public sealed class McpServerAgentSelectionItem
{
    /// <summary>
    /// Gets or sets the agent name, which is also the tool name MCP clients see.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the agent description shown to MCP clients.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this agent is exposed.
    /// </summary>
    public bool IsSelected { get; set; }
}
