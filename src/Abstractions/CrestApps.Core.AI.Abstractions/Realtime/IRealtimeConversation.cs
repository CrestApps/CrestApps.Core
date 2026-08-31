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
    /// The model finished generating a response for a turn. <see cref="RealtimeConversationEvent.ResponseStatus"/> carries the outcome.
    /// </summary>
    ResponseCompleted,

    /// <summary>
    /// An error was reported by the session.
    /// </summary>
    Error,
}
