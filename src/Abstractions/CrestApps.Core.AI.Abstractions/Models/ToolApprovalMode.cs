namespace CrestApps.Core.AI.Models;

/// <summary>
/// How often a person must approve calls to a tool that requires an approval. The numeric order is the strictness:
/// the lowest value is the strictest. The values are stored, so they never change.
/// </summary>
public enum ToolApprovalMode
{
    /// <summary>
    /// Ask before every call. This is the default.
    /// </summary>
    EveryCall = 0,

    /// <summary>
    /// Ask before the first call in a chat session. Reserved for the approval policy's session grants: until they
    /// exist, this mode asks before every call, like <see cref="EveryCall"/>.
    /// </summary>
    OncePerSession = 1,

    /// <summary>
    /// Run without asking. Each such call is logged and counted (see <see cref="AITelemetry.ToolAutoApprovals"/>).
    /// It never lowers a tool's approval floor (see <see cref="ToolRegistryEntry.ApprovalFloor"/>).
    /// </summary>
    Never = 2,
}
