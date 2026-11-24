using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// The approval policy a request runs with, resolved from a profile's <see cref="AIToolApprovalSettings"/>: the tool
/// kinds whose calls run without asking. A request with no policy asks before every call of a tool that requires an
/// approval.
/// </summary>
/// <remarks>
/// A policy only lowers approvals that come from a tool's kind. It never lowers a tool's approval floor (an explicit
/// <c>.RequireApproval()</c>, or a consequential WebMCP page tool), see <see cref="ToolRegistryEntry.ApprovalFloor"/>.
/// </remarks>
public sealed class ToolApprovalPolicy
{
    private readonly HashSet<AIToolKind> _runWithoutAsking;

    private ToolApprovalPolicy(HashSet<AIToolKind> runWithoutAsking)
    {
        _runWithoutAsking = runWithoutAsking;
    }

    /// <summary>
    /// Gets the tool kinds whose calls run without asking.
    /// </summary>
    public IReadOnlyCollection<AIToolKind> KindsRunWithoutAsking => _runWithoutAsking;

    /// <summary>
    /// Resolves the policy of a profile's settings.
    /// </summary>
    /// <param name="settings">The profile's tool approval settings, or <see langword="null"/>.</param>
    /// <returns>The policy, or <see langword="null"/> when every call that requires an approval asks.</returns>
    public static ToolApprovalPolicy FromSettings(AIToolApprovalSettings settings)
    {
        if (settings?.Kinds is not { Count: > 0 })
        {
            return null;
        }

        var kinds = new HashSet<AIToolKind>();

        foreach (var (name, rule) in settings.Kinds)
        {
            // OncePerSession asks before every call until session grants exist, so only Never runs without asking.
            if (rule?.Mode == ToolApprovalMode.Never &&
                Enum.TryParse<AIToolKind>(name, ignoreCase: true, out var kind) &&
                Enum.IsDefined(kind))
            {
                kinds.Add(kind);
            }
        }

        return kinds.Count > 0 ? new ToolApprovalPolicy(kinds) : null;
    }

    /// <summary>
    /// Combines the policy of the profile that calls an agent with the agent's own policy. The stricter one wins:
    /// a call inside the agent runs without asking only when both the caller and the agent allow it. Otherwise one
    /// shared agent could take approvals away from every profile that uses it.
    /// </summary>
    /// <param name="callerPolicy">The policy of the calling request, or <see langword="null"/> when it has none
    /// (or there is no calling profile, such as an MCP client that calls the agent).</param>
    /// <param name="agentPolicy">The agent profile's policy, or <see langword="null"/>.</param>
    /// <returns>The combined policy, or <see langword="null"/> when every call asks.</returns>
    public static ToolApprovalPolicy ForAgent(ToolApprovalPolicy callerPolicy, ToolApprovalPolicy agentPolicy)
    {
        if (callerPolicy is null || agentPolicy is null)
        {
            return null;
        }

        var kinds = new HashSet<AIToolKind>(callerPolicy._runWithoutAsking);
        kinds.IntersectWith(agentPolicy._runWithoutAsking);

        return kinds.Count > 0 ? new ToolApprovalPolicy(kinds) : null;
    }

    /// <summary>
    /// Determines whether a call of a tool runs without asking under this policy.
    /// </summary>
    /// <param name="entry">The registry entry of the tool.</param>
    /// <returns>
    /// <see langword="true"/> when the tool requires an approval only because of its kind, and the policy lets that
    /// kind run without asking; otherwise <see langword="false"/>.
    /// </returns>
    public bool RunsWithoutAsking(ToolRegistryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.RequiresApproval &&
            entry.ApprovalFloor is null &&
            _runWithoutAsking.Contains(entry.Kind);
    }
}
