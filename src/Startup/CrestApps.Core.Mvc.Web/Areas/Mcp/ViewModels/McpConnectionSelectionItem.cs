namespace CrestApps.Core.Mvc.Web.Areas.Mcp.ViewModels;

public sealed class McpConnectionSelectionItem
{
    public string ItemId { get; set; }

    public string DisplayText { get; set; }

    public string Source { get; set; }

    public bool IsSelected { get; set; }

    /// <summary>
    /// Whether the profile takes every tool the connection exposes (the default) or only <see cref="Tools"/>
    /// marked selected.
    /// </summary>
    public bool UseAllTools { get; set; } = true;

    /// <summary>
    /// Whether <see cref="Tools"/> was loaded from the server for this render.
    /// </summary>
    public bool ToolsLoaded { get; set; }

    /// <summary>
    /// Why the tools could not be loaded, when they could not.
    /// </summary>
    public string ToolsError { get; set; }

    public List<McpToolSelectionItem> Tools { get; set; } = [];
}

/// <summary>
/// One tool exposed by an MCP connection, as offered for selection on a profile.
/// </summary>
public sealed class McpToolSelectionItem
{
    public string Name { get; set; }

    public string Description { get; set; }

    public bool IsSelected { get; set; }
}
