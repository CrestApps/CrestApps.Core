using System.Security.Claims;

namespace CrestApps.Core.AI.Tooling;

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
