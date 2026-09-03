#nullable enable
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Transport-agnostic sink that receives the outbound side of a realtime chat conversation. A SignalR
/// hub implements this over its connected client; tests implement it to capture what was sent. Keeping it
/// separate from the driving logic lets <see cref="RealtimeChatSessionRunner"/> be exercised without a
/// live connection.
/// </summary>
public interface IRealtimeConversationSink
{
    /// <summary>
    /// Delivers a chunk of the assistant's synthesized audio for playback.
    /// </summary>
    Task AssistantAudioAsync(string identifier, ReadOnlyMemory<byte> audio, CancellationToken cancellationToken);

    /// <summary>
    /// Delivers a completed user utterance transcript, filling in the placeholder created for
    /// <paramref name="turnId"/>.
    /// </summary>
    /// <param name="identifier">The session or interaction identifier.</param>
    /// <param name="turnId">The turn the transcript belongs to.</param>
    /// <param name="text">The transcribed text.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task UserTranscriptAsync(string identifier, string turnId, string text, CancellationToken cancellationToken);

    /// <summary>
    /// Signals that a pending user turn produced nothing worth showing — transcription failed, or the utterance
    /// was never answered — so the client can remove its placeholder.
    /// </summary>
    /// <param name="identifier">The session or interaction identifier.</param>
    /// <param name="turnId">The turn to drop.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task UserTurnDroppedAsync(string identifier, string turnId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delivers an incremental piece of the assistant's spoken-response transcript.
    /// </summary>
    Task AssistantTranscriptDeltaAsync(
        string identifier,
        string messageId,
        string text,
        string responseId,
        Dictionary<string, AICompletionReference>? references,
        CancellationToken cancellationToken);

    /// <summary>
    /// Signals that the assistant's spoken response for a turn is complete.
    /// </summary>
    Task AssistantCompletedAsync(
        string identifier,
        string messageId,
        Dictionary<string, AICompletionReference>? references,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the amount of assistant audio, in milliseconds, that has been handed to this sink but not yet heard
    /// by the user (still queued for paced playback). Transports that hand audio straight to the client return
    /// zero. Used to decide when a response is really over for half-duplex turn-taking.
    /// </summary>
    int PendingPlaybackMs => 0;

    /// <summary>
    /// Signals that the provider session is open and the conversation is live, so the client can move from
    /// "connecting" to "listening".
    /// </summary>
    Task SessionReadyAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signals that the session has ended and no further events will arrive, so the client can release the
    /// microphone and return to idle instead of streaming into a dead session.
    /// </summary>
    /// <param name="identifier">The session or interaction identifier.</param>
    /// <param name="reason">Why the session ended (for example <c>completed</c>, <c>cancelled</c>, <c>error</c>).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SessionEndedAsync(string identifier, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signals that the user began speaking (may interrupt the assistant).
    /// </summary>
    Task SpeechStartedAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signals that a user utterance has been captured and is being transcribed, so the client can show a
    /// placeholder in the right place in the conversation. The transcript for short replies often arrives after
    /// the assistant has already answered, so a bubble added only on the transcript lands underneath the reply it
    /// prompted.
    /// </summary>
    /// <param name="identifier">The session or interaction identifier.</param>
    /// <param name="turnId">The turn the placeholder belongs to, matching the later transcript.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task UserTurnPendingAsync(string identifier, string turnId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops any buffered/still-playing assistant audio so playback stops immediately. Used when a newer response
    /// supersedes one whose audio is still draining (with barge-in off, a follow-up accepted after the previous
    /// reply's text finished must not keep playing the old audio). 
    /// </summary>
    Task FlushPlaybackAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delivers an error to the client.
    /// </summary>
    Task ErrorAsync(string message, CancellationToken cancellationToken = default);
}
