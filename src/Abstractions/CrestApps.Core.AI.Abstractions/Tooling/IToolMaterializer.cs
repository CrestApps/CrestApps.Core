using System.Security.Claims;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Materializes scoped <see cref="ToolRegistryEntry"/> instances into the concrete <see cref="AITool"/>
/// set handed to a model. This is the single shared implementation of "scoped entries → tools" used by
/// both the text completion path (function-invocation completion handler) and the realtime session
/// configurator, so tool selection, per-user authorization, and de-duplication behave identically
/// regardless of how the model is ultimately invoked.
/// </summary>
public interface IToolMaterializer
{
    /// <summary>
    /// Resolves the given scoped entries into <see cref="AITool"/> instances.
    /// </summary>
    /// <param name="entries">The already-scoped tool registry entries to materialize.</param>
    /// <param name="options">Controls per-user access enforcement.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<ToolMaterializationResult> MaterializeAsync(
        IReadOnlyList<ToolRegistryEntry> entries,
        ToolMaterializationOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Controls how <see cref="IToolMaterializer"/> materializes tools.
/// </summary>
public sealed class ToolMaterializationOptions
{
    /// <summary>
    /// Gets the default options: no per-user access enforcement. This matches AI Session / profile-driven
    /// requests, where the profile is the authorization boundary (including realtime sessions).
    /// </summary>
    public static ToolMaterializationOptions Default { get; } = new();

    /// <summary>
    /// Gets a value indicating whether each listable (user-selectable) tool is checked against
    /// <see cref="IAIToolAccessEvaluator"/> for <see cref="User"/>. Used for Chat Interaction requests,
    /// where a caller-persisted tool selection must be re-verified at send time. System and
    /// hidden/dependency tools are never checked.
    /// </summary>
    public bool EnforceListableAccess { get; init; }

    /// <summary>
    /// Gets the principal to authorize when <see cref="EnforceListableAccess"/> is <see langword="true"/>.
    /// A <see langword="null"/> principal means there is no caller (e.g., a background task), so the
    /// request is treated as trusted server-side and no check is applied.
    /// </summary>
    public ClaimsPrincipal User { get; init; }
}

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
