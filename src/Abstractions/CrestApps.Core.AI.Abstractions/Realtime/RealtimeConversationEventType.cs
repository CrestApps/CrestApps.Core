namespace CrestApps.Core.AI.Realtime;

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
