namespace CrestApps.Core.AI.Models;

/// <summary>
/// An approval rule of a profile: how often a person must approve calls to the tools it covers.
/// </summary>
public sealed class ToolApprovalRule
{
    /// <summary>
    /// Gets or sets how often a person must approve a call. The default asks before every call.
    /// </summary>
    public ToolApprovalMode Mode { get; set; } = ToolApprovalMode.EveryCall;
}
