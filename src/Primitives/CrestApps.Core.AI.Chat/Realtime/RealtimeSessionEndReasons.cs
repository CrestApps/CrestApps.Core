namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// The reasons a realtime session can end, reported to the client so it can release the microphone and explain
/// what happened instead of streaming into a session that is no longer there.
/// </summary>
public static class RealtimeSessionEndReasons
{
    /// <summary>
    /// The provider closed the event stream and the conversation finished normally.
    /// </summary>
    public const string Completed = "completed";

    /// <summary>
    /// The session was cancelled — the user stopped it, the connection dropped, or the peer closed.
    /// </summary>
    public const string Cancelled = "cancelled";

    /// <summary>
    /// The session ended because of an unexpected failure.
    /// </summary>
    public const string Error = "error";

    /// <summary>
    /// The session was closed because nobody spoke for long enough. A realtime session bills for an open provider
    /// connection whether or not anyone is talking, so a forgotten tab is ended rather than left running.
    /// </summary>
    public const string Idle = "idle";
}
