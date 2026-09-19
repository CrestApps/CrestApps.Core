namespace CrestApps.Core.Blazor.Web.ViewModels;

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
    /// Whether <see cref="Tools"/> has been loaded from the server. Loading is on demand: it costs a round-trip
    /// to the MCP server, so it only happens for a connection whose individual tools are being chosen.
    /// </summary>
    public bool ToolsLoaded { get; set; }

    /// <summary>
    /// Why the tools could not be loaded, when they could not.
    /// </summary>
    public string ToolsError { get; set; }

    public List<McpToolSelectionItem> Tools { get; set; } = [];
}
