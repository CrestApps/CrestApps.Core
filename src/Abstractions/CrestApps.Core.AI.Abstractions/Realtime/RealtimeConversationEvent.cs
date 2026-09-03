namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// A single provider-neutral event emitted by an <see cref="IRealtimeConversation"/>.
/// </summary>
public sealed class RealtimeConversationEvent
{
    /// <summary>
    /// Gets the event kind.
    /// </summary>
    public required RealtimeConversationEventType Type { get; init; }

    /// <summary>
    /// Gets the assistant audio bytes for <see cref="RealtimeConversationEventType.AssistantAudioDelta"/>.
    /// </summary>
    public ReadOnlyMemory<byte> Audio { get; init; }

    /// <summary>
    /// Gets the transcript text for user/assistant transcript events.
    /// </summary>
    public string Text { get; init; }

    /// <summary>
    /// Gets the response status for <see cref="RealtimeConversationEventType.ResponseCompleted"/> (e.g. "completed", "cancelled").
    /// </summary>
    public string ResponseStatus { get; init; }

    /// <summary>
    /// Gets the error message for <see cref="RealtimeConversationEventType.Error"/>.
    /// </summary>
    public string ErrorMessage { get; init; }

    /// <summary>
    /// Gets the provider's conversation item id the event belongs to, when the provider supplies one. This is the
    /// only stable way to pair a user utterance with the transcript that arrives for it later, and the only way to
    /// name the assistant item to truncate on a barge-in; the arrival order of events cannot be relied on for
    /// either, because transcription lags the reply it belongs to and can fail outright.
    /// </summary>
    public string ItemId { get; init; }

    /// <summary>
    /// Gets the provider's response id for response lifecycle events, when the provider supplies one.
    /// </summary>
    public string ResponseId { get; init; }
}
