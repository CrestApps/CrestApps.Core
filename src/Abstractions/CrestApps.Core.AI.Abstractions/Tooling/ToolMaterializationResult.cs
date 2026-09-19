using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// The result of a tool-materialization pass.
/// </summary>
public sealed class ToolMaterializationResult
{
    /// <summary>
    /// Gets the materialized tools, de-duplicated by function name and ordered so local/system tools
    /// precede MCP tools.
    /// </summary>
    public IReadOnlyList<AITool> Tools { get; init; } = [];

    /// <summary>
    /// Gets the names of listable tools that were excluded because the caller was not authorized to use
    /// them. Empty when <see cref="ToolMaterializationOptions.EnforceListableAccess"/> is disabled.
    /// </summary>
    public IReadOnlyList<string> DeniedToolNames { get; init; } = [];
}
