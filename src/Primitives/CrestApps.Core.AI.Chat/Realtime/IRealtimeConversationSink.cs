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
    /// Delivers a completed user utterance transcript.
    /// </summary>
    Task UserTranscriptAsync(string identifier, string text, CancellationToken cancellationToken);

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
    /// Signals that the user began speaking (may interrupt the assistant).
    /// </summary>
    Task SpeechStartedAsync(string identifier, CancellationToken cancellationToken);

    /// <summary>
    /// Drops any buffered/still-playing assistant audio so playback stops immediately. Used when a newer response
    /// supersedes one whose audio is still draining (with barge-in off, a follow-up accepted after the previous
    /// reply's text finished must not keep playing the old audio).
    /// </summary>
    Task FlushPlaybackAsync(string identifier, CancellationToken cancellationToken);

    /// <summary>
    /// Delivers an error to the client.
    /// </summary>
    Task ErrorAsync(string message, CancellationToken cancellationToken);
}
