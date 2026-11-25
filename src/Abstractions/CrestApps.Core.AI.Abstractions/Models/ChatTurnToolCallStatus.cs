namespace CrestApps.Core.AI.Models;

/// <summary>
/// Where a tool call of an assistant message stands.
/// </summary>
public enum ChatTurnToolCallStatus
{
    /// <summary>
    /// The model asked for the call and it has no result yet.
    /// </summary>
    Requested,

    /// <summary>
    /// The call ran and has a result.
    /// </summary>
    Completed,

    /// <summary>
    /// The call waits for a person to approve it.
    /// </summary>
    AwaitingApproval,

    /// <summary>
    /// The call was approved, and the run that resumed made it. A person approved it, or, for a call that needs no
    /// approval of its own (see <see cref="ChatTurnToolCall.NeedsNoApproval"/>), the run that resumed did.
    /// </summary>
    Approved,

    /// <summary>
    /// A person rejected the call, and it did not run.
    /// </summary>
    Rejected,

    /// <summary>
    /// The call is to a tool the client runs, and waits for the client's result.
    /// </summary>
    AwaitingClient,
}
