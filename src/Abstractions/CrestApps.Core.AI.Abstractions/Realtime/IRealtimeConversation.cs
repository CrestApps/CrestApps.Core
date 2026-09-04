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
    /// Appends a chunk of the user's input audio (PCM16) to the session's input buffer.
    /// </summary>
    /// <param name="audio">The PCM16 audio bytes.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SendAudioAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken = default);

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

/// <summary>
/// Identifies the kind of a <see cref="RealtimeConversationEvent"/>.
/// </summary>
public enum RealtimeConversationEventType
{
    /// <summary>
    /// A chunk of the assistant's synthesized output audio (PCM16) to play back.
    /// </summary>
    AssistantAudioDelta,

    /// <summary>
    /// A completed transcript of a user utterance (from input-audio transcription).
    /// </summary>
    UserTranscript,

    /// <summary>
    /// Input-audio transcription failed for a user utterance, so no <see cref="UserTranscript"/> will follow for
    /// it. Emitted so turn bookkeeping that pairs utterances with transcripts stays aligned.
    /// </summary>
    UserTranscriptFailed,

    /// <summary>
    /// An incremental piece of the assistant's spoken-response transcript.
    /// </summary>
    AssistantTranscriptDelta,

    /// <summary>
    /// The completed transcript of the assistant's spoken response for a turn.
    /// </summary>
    AssistantTranscriptDone,

    /// <summary>
    /// The user began speaking (voice-activity detection), which may interrupt the assistant.
    /// </summary>
    UserSpeechStarted,

    /// <summary>
    /// The provider committed the user's utterance as a conversation item and will transcribe it.
    /// <see cref="RealtimeConversationEvent.ItemId"/> names the item, so the transcript that arrives later — or
    /// the failure that arrives instead — can be paired with the right utterance.
    /// </summary>
    UserTurnCommitted,

    /// <summary>
    /// The model started generating a response for a turn (the provider created a response). Marks the point
    /// after which further user speech cannot be answered until the response completes (unless interruption is on).
    /// </summary>
    ResponseStarted,

    /// <summary>
    /// The model finished generating a response for a turn. <see cref="RealtimeConversationEvent.ResponseStatus"/> carries the outcome.
    /// </summary>
    ResponseCompleted,

    /// <summary>
    /// An error was reported by the session.
    /// </summary>
    Error,
}
