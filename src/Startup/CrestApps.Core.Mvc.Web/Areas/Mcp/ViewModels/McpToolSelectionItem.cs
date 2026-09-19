namespace CrestApps.Core.Mvc.Web.Areas.Mcp.ViewModels;

/// <summary>
/// One tool exposed by an MCP connection, as offered for selection on a profile.
/// </summary>
public sealed class McpToolSelectionItem
{
    public string Name { get; set; }

    public string Description { get; set; }

    public bool IsSelected { get; set; }
}
