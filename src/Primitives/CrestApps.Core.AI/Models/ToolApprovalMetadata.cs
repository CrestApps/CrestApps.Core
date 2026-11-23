namespace CrestApps.Core.AI.Models;

/// <summary>
/// Stores whether an AI profile asks before running the tools that require an approval.
/// Stored in Properties via Put/As pattern, parallel to <see cref="FunctionInvocationMetadata"/>.
/// </summary>
public sealed class ToolApprovalMetadata
{
    /// <summary>
    /// Gets or sets a value indicating whether the profile runs its tools without asking. When it is
    /// <see langword="false"/> (the default), each tool that requires an approval asks the person in the chat first,
    /// and never runs where nobody can be asked. When it is <see langword="true"/>, those tools run at once, also
    /// where nobody can be asked, such as an MCP client that calls an agent or a voice session. Only the approval is skipped: the
    /// tools are still limited to the ones picked for the profile, and the access checks still apply.
    /// </summary>
    public bool RunWithoutAsking { get; set; }
}
