namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// A live speech-to-speech conversation: send the user's microphone audio in, and read a stream of
/// provider-neutral <see cref="RealtimeConversationEvent"/>s out (assistant audio, both-ends transcript,
/// turn/error signals). Tool calls raised by the model are executed transparently while the event stream
/// is being enumerated.
/// </summary>
public interface IRealtimeConversation : IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether the provider creates a response on its own as soon as it decides the user
    /// has finished speaking. When <see langword="false"/> the session was opened with response creation deferred
    /// to the host, which must call <see cref="RequestResponseAsync"/> once it has finished preparing the turn —
    /// otherwise the model never answers.
    /// </summary>
    bool RespondsAutomatically { get; }

    /// <summary>
    /// Appends a chunk of the user's input audio (PCM16) to the session's input buffer.
    /// </summary>
    /// <param name="audio">The PCM16 audio bytes.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SendAudioAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves knowledge relevant to what the user just said and adds it to the conversation, so the model
    /// answers from the knowledge base instead of from whatever it happens to remember. Does nothing when the
    /// session was not opened with grounding (see <see cref="RespondsAutomatically"/>).
    /// </summary>
    /// <param name="utterance">The transcript of the user's utterance.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when retrieved context was added to the conversation; otherwise <see langword="false"/>.</returns>
    Task<bool> GroundTurnAsync(string utterance, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the model to answer now. Only needed when <see cref="RespondsAutomatically"/> is
    /// <see langword="false"/>; calling it on a session that responds automatically would produce a second,
    /// duplicate reply, so it is a no-op there.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task RequestResponseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the model to speak when the user has not just spoken — to open a call, or to break a silence that
    /// has gone on too long.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="RequestResponseAsync"/> refuses this on a session that responds automatically, and is right to:
    /// on a turn the provider has already answered, a second response is a duplicate. That reasoning does not
    /// hold when the provider has not answered anything — before the first turn, or after one that produced no
    /// transcript — and those are the moments when somebody has to speak or nobody will. An outbound call left
    /// the customer listening to silence until they said "hello" first, and a caller whose reply was lost waited
    /// for an assistant that was waiting for them.
    /// </para>
    /// <para>
    /// Only call this when no response is in flight and the user's turn is not about to be answered; on a
    /// session that answers by itself, anything else produces two replies talking over each other.
    /// </para>
    /// </remarks>
    /// <param name="instructions">What the model should say, or <see langword="null"/> to let it decide from the conversation so far.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task RequestUnpromptedResponseAsync(string instructions = null, CancellationToken cancellationToken = default)
        => RequestResponseAsync(cancellationToken);

    /// <summary>
    /// Asks the model to speak a short acknowledgement — "let me look that up" — to cover a wait the user would
    /// otherwise hear as silence.
    /// </summary>
    /// <remarks>
    /// The response is requested out-of-band: it is spoken but never added to the conversation, so the model does
    /// not later see itself having said it, and it cannot call tools or run long. Like
    /// <see cref="RequestResponseAsync"/> it is a no-op on a session that responds automatically, which never has
    /// a wait to cover.
    /// </remarks>
    /// <param name="instructions">What the model should say.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task RequestAcknowledgementAsync(string instructions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams conversation events (assistant audio, transcripts, turn and error signals) until the
    /// session ends or the token is cancelled. Enumerating this stream also drives the realtime tool
    /// loop, so it must be enumerated within the caller's <c>AIInvocationScope</c>.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    IAsyncEnumerable<RealtimeConversationEvent> GetEventsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the provider that only the first <paramref name="audioEndMs"/> milliseconds of an assistant item were
    /// actually heard, so the rest is removed from the conversation. Without this, an interrupted reply stays in
    /// the model's context in full and it believes it said things the user never heard — which makes follow-ups
    /// like "repeat that" or "what did you just say?" wrong.
    /// </summary>
    /// <param name="itemId">The assistant conversation item that was interrupted.</param>
    /// <param name="audioEndMs">How much of the item's audio the user actually heard, in milliseconds.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task TruncateAssistantAudioAsync(string itemId, int audioEndMs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the provider's turn-detection settings on the running session, so a user who toggles barge-in or
    /// retunes voice activity mid-conversation gets the new behaviour immediately instead of on their next
    /// session — and so the provider does not keep interrupting itself after the client has stopped allowing it.
    /// </summary>
    /// <param name="allowInterruption">Whether the model may be interrupted while speaking.</param>
    /// <param name="silenceDurationMs">The silence, in milliseconds, that ends a user turn, when specified (server VAD only).</param>
    /// <param name="vadThreshold">The voice-activity detection threshold (0.0-1.0), when specified (server VAD only).</param>
    /// <param name="turnDetectionType">
    /// The turn-detection algorithm to switch to (see <see cref="RealtimeTurnDetectionTypes"/>), or
    /// <see langword="null"/> to keep the session's current algorithm.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task UpdateTurnDetectionAsync(
        bool allowInterruption,
        int? silenceDurationMs,
        float? vadThreshold,
        string turnDetectionType = null,
        CancellationToken cancellationToken = default);
}
