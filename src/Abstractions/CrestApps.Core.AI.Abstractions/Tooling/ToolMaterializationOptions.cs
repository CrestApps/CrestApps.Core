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

    /// <summary>
    /// Gets a value indicating whether the caller can ask a person to approve a tool call. When it is
    /// <see langword="true"/>, the tools whose entry sets <see cref="Models.ToolRegistryEntry.RequiresApproval"/>
    /// are wrapped in <see cref="Microsoft.Extensions.AI.ApprovalRequiredAIFunction"/>, so the function invocation
    /// pauses for an approval instead of running them.
    /// </summary>
    public bool SupportsToolApproval { get; init; }

    /// <summary>
    /// Gets a value indicating whether a tool that requires an approval must never run without one. When it is
    /// <see langword="true"/> and <see cref="SupportsToolApproval"/> is <see langword="false"/>, such a tool is
    /// replaced with a function of the same name and schema that does not run the tool and tells the model that
    /// this conversation cannot ask a person for the approval. The run of an agent uses it. When it is
    /// <see langword="false"/> (the default), a caller that cannot ask runs the tool as before.
    /// </summary>
    public bool EnforceToolApproval { get; init; }
}
