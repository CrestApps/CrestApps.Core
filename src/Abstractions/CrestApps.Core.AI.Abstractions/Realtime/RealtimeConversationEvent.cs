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
}
