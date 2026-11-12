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
    /// A person approved the call, and the run that resumed made it.
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
