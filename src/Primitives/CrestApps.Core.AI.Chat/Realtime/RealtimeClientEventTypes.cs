namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// The realtime lifecycle event names sent to the browser over the single <c>ReceiveRealtimeEvent</c> client
/// method. One method with a typed name keeps the hub client interfaces small while giving the browser the
/// state machine it needs (connecting, listening, interrupted, ended).
/// </summary>
public static class RealtimeClientEventTypes
{
    /// <summary>
    /// The provider session is open; the client can move from "connecting" to "listening".
    /// </summary>
    public const string SessionReady = "session_ready";

    /// <summary>
    /// The session ended. The payload carries the reason (see <see cref="RealtimeSessionEndReasons"/>).
    /// </summary>
    public const string SessionEnded = "session_ended";

    /// <summary>
    /// The provider detected the start of user speech. With barge-in on this is an interruption: the client must
    /// stop playback immediately.
    /// </summary>
    public const string SpeechStarted = "speech_started";

    /// <summary>
    /// Buffered assistant audio has been superseded and must be dropped so the newest reply plays instead.
    /// </summary>
    public const string PlaybackFlush = "playback_flush";

    /// <summary>
    /// A user utterance has been captured and is being transcribed. The payload is the turn id. The client shows a
    /// placeholder now so the prompt sits above the reply it produces — transcription lags the spoken answer, so a
    /// bubble added only when the transcript arrives lands underneath its own reply.
    /// </summary>
    public const string UserTurnPending = "user_turn_pending";

    /// <summary>
    /// A pending user turn produced nothing worth showing (transcription failed, or the utterance was never
    /// answered). The payload is the turn id; the client removes its placeholder.
    /// </summary>
    public const string UserTurnDropped = "user_turn_dropped";
}
