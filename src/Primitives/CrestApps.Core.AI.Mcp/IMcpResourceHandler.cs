namespace CrestApps.Core.AI.Mcp;

/// <summary>
/// Interface for handling MCP resource events like exporting.
/// </summary>
public interface IMcpResourceHandler
{
    /// <summary>
    /// Called during resource export to allow modification of export data.
    /// </summary>
    /// <param name="context">The context.</param>
    void Exporting(ExportingMcpResourceContext context);
}
