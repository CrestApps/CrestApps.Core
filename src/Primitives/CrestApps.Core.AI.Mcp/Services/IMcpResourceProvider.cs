using ModelContextProtocol.Server;

namespace CrestApps.Core.AI.Mcp.Services;

/// <summary>
/// Provides additional MCP resources from external sources such as
/// agent skill files, plugins, or other discovery mechanisms.
/// Implementations are collected via <c>IEnumerable&lt;IMcpResourceProvider&gt;</c>
/// and merged into the default <see cref="IMcpServerResourceService"/>.
/// </summary>
public interface IMcpResourceProvider
{
    /// <summary>
    /// Asynchronously discovers and returns MCP resource instances.
    /// </summary>
    Task<IReadOnlyList<McpServerResource>> GetResourcesAsync();
}
