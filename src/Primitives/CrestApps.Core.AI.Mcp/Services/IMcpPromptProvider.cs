using ModelContextProtocol.Server;

namespace CrestApps.Core.AI.Mcp.Services;

/// <summary>
/// Provides additional MCP prompts from external sources such as
/// agent skill files, plugins, or other discovery mechanisms.
/// Implementations are collected via <c>IEnumerable&lt;IMcpPromptProvider&gt;</c>
/// and merged into the default <see cref="IMcpServerPromptService"/>.
/// </summary>
public interface IMcpPromptProvider
{
    /// <summary>
    /// Asynchronously discovers and returns MCP prompt instances.
    /// </summary>
    Task<IReadOnlyList<McpServerPrompt>> GetPromptsAsync();
}
