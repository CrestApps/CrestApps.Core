using System.Text.Json.Nodes;
using CrestApps.Core.AI.Mcp.Models;

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

/// <summary>
/// Context provided during MCP resource export.
/// </summary>
public sealed class ExportingMcpResourceContext
{
    /// <summary>
    /// The resource being exported.
    /// </summary>
    public readonly McpResource Resource;

    /// <summary>
    /// The JSON data being exported. Can be modified to remove sensitive data.
    /// </summary>
    public readonly JsonObject ExportData;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportingMcpResourceContext"/> class.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="exportData">The export data.</param>
    public ExportingMcpResourceContext(
        McpResource resource,
        JsonObject exportData)
    {

        ArgumentNullException.ThrowIfNull(resource);

        Resource = resource;
        ExportData = exportData ?? [];
    }
}
