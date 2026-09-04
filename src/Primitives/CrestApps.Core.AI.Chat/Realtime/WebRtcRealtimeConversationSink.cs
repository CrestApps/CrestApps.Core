#nullable enable
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Realtime;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// An <see cref="IRealtimeConversationSink"/> for the server-relay WebRTC transport. Assistant audio is written to
/// the WebRTC peer (so it reaches the browser as an Opus media track, giving the browser real echo cancellation);
/// every other event — transcripts, speech-started, errors — is delegated to the inner SignalR sink, keeping the
/// event/transcript path identical to the WebSocket transport.
/// </summary>
internal sealed class WebRtcRealtimeConversationSink : IRealtimeConversationSink
{
    private readonly IRealtimeConversationSink _inner;
    private readonly IWebRtcRealtimePeer _peer;

    public WebRtcRealtimeConversationSink(IRealtimeConversationSink inner, IWebRtcRealtimePeer peer)
    {
        _inner = inner;
        _peer = peer;
    }

    /// <inheritdoc />
    public int PendingPlaybackMs => _peer.QueuedPlaybackMs;

    public Task AssistantAudioAsync(string identifier, ReadOnlyMemory<byte> audio, CancellationToken cancellationToken)
    {
        _peer.SendAudio(audio);

        return Task.CompletedTask;
    }

    public Task SessionReadyAsync(string identifier, CancellationToken cancellationToken)
        => _inner.SessionReadyAsync(identifier, cancellationToken);

    public Task SessionEndedAsync(string identifier, string reason, CancellationToken cancellationToken)
        => _inner.SessionEndedAsync(identifier, reason, cancellationToken);

    public Task UserTranscriptAsync(string identifier, string turnId, string text, CancellationToken cancellationToken)
        => _inner.UserTranscriptAsync(identifier, turnId, text, cancellationToken);

    public Task UserTurnPendingAsync(string identifier, string turnId, CancellationToken cancellationToken)
        => _inner.UserTurnPendingAsync(identifier, turnId, cancellationToken);

    public Task UserTurnDroppedAsync(string identifier, string turnId, CancellationToken cancellationToken)
        => _inner.UserTurnDroppedAsync(identifier, turnId, cancellationToken);

    public Task AssistantTranscriptDeltaAsync(string identifier, string messageId, string text, string responseId, Dictionary<string, AICompletionReference>? references, CancellationToken cancellationToken)
        => _inner.AssistantTranscriptDeltaAsync(identifier, messageId, text, responseId, references, cancellationToken);

    public Task AssistantCompletedAsync(string identifier, string messageId, Dictionary<string, AICompletionReference>? references, CancellationToken cancellationToken)
        => _inner.AssistantCompletedAsync(identifier, messageId, references, cancellationToken);

    public Task SpeechStartedAsync(string identifier, CancellationToken cancellationToken)
    {
        // Barge-in: drop the buffered tail of the interrupted response so playback stops immediately and the new
        // response starts cleanly, instead of the browser finishing the old (paced) audio first.
        _peer.FlushPlayback();

        return _inner.SpeechStartedAsync(identifier, cancellationToken);
    }

    public Task FlushPlaybackAsync(string identifier, CancellationToken cancellationToken)
    {
        // A newer response has superseded one whose paced audio is still draining — drop the old audio so the
        // browser plays the newest reply instead of finishing the stale one.
        _peer.FlushPlayback();

        return _inner.FlushPlaybackAsync(identifier, cancellationToken);
    }

    public Task ErrorAsync(string message, CancellationToken cancellationToken)
        => _inner.ErrorAsync(message, cancellationToken);
}
