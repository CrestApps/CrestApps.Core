#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only.
#nullable enable
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Realtime;
using CrestApps.Core.Services;
using Cysharp.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Drives a speech-to-speech chat session: it starts a realtime conversation through
/// <see cref="IRealtimeOrchestrator"/>, relays the user's microphone audio into it, forwards the model's
/// audio and both-ends transcript to an <see cref="IRealtimeConversationSink"/>, and persists each
/// completed turn through an <see cref="IRealtimeTurnStore"/> so the conversation appears in history
/// exactly like a text chat. The turn store makes this reusable for both AI Chat sessions and chat
/// interactions.
/// </summary>
/// <remarks>
/// This type is host-agnostic and does not touch SignalR, so it can be unit-tested with a fake
/// orchestrator, sink, and turn store. The caller must establish an <see cref="AIInvocationScope"/> before
/// calling <see cref="RunAsync"/> and keep it alive for the duration, so tools invoked mid-session observe
/// the correct ambient context and their citations are captured onto assistant turns.
/// </remarks>
public sealed class RealtimeChatSessionRunner
{
    private readonly IRealtimeOrchestrator _orchestrator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RealtimeChatSessionRunner> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RealtimeChatSessionRunner"/> class.
    /// </summary>
    public RealtimeChatSessionRunner(
        IRealtimeOrchestrator orchestrator,
        TimeProvider timeProvider,
        ILogger<RealtimeChatSessionRunner> logger)
    {
        _orchestrator = orchestrator;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Runs the conversation to completion (until the audio input ends, the session closes, or the token
    /// is cancelled).
    /// </summary>
    /// <param name="context">The run context (resource, session, voice, hooks).</param>
    /// <param name="turnStore">The store that persists completed turns.</param>
    /// <param name="audioInput">The stream of the user's PCM16 microphone audio.</param>
    /// <param name="sink">The outbound sink for audio and transcript.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task RunAsync(
        RealtimeChatRunContext context,
        IRealtimeTurnStore turnStore,
        IAsyncEnumerable<ReadOnlyMemory<byte>> audioInput,
        IRealtimeConversationSink sink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(turnStore);
        ArgumentNullException.ThrowIfNull(audioInput);
        ArgumentNullException.ThrowIfNull(sink);

        await using var conversation = await _orchestrator.StartAsync(
            new RealtimeOrchestrationRequest
            {
                Resource = context.Resource,
                RealtimeDeploymentName = context.RealtimeDeploymentName,
                ChatSession = context.ChatSession,
                Interaction = context.Interaction,
                Voice = context.Voice,
                SpeechLanguage = context.SpeechLanguage,
                SilenceDurationMs = context.SilenceDurationMs,
                AllowInterruption = context.AllowInterruption,
                VadThreshold = context.VadThreshold,
            },
            cancellationToken);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var inbound = PumpInputAsync(conversation, audioInput, linkedCts.Token);
        var outbound = PumpOutputAsync(context, turnStore, conversation, sink, context.SessionId, linkedCts.Token);

        await Task.WhenAny(inbound, outbound);
        await linkedCts.CancelAsync();

        try
        {
            await Task.WhenAll(inbound, outbound);
        }
        catch (Exception ex) when (IsExpectedShutdownException(ex))
        {
            // Both pumps are being cancelled as the session tears down. A cancelled or aborted transport read/
            // write here is expected — a user-requested stop must not surface as an error.
        }
    }

    // Transport aborts that are expected while a realtime session is shutting down (for example when the user
    // stops the conversation): cancellation, or the underlying socket being aborted mid read/write.
    private static bool IsExpectedShutdownException(Exception ex)
        => ex is OperationCanceledException
            or System.IO.IOException
            or System.Net.Sockets.SocketException
            or System.Net.WebSockets.WebSocketException;

    // Provider errors that are an expected consequence of open-mic turn-taking rather than a real failure, so
    // they should be logged but never surfaced to the user as a chat message.
    private static bool IsBenignRealtimeError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        // "Conversation already has an active response in progress: resp_..." — barge-in off, the user spoke over
        // the model and the provider refused to start a second overlapping response.
        // "Cancellation failed: no active response found" / "no active response" — a barge-in cancel landed just
        // after the response had already finished on its own.
        return message.Contains("active response in progress", StringComparison.OrdinalIgnoreCase)
            || message.Contains("no active response", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task PumpInputAsync(
        IRealtimeConversation conversation,
        IAsyncEnumerable<ReadOnlyMemory<byte>> audioInput,
        CancellationToken cancellationToken)
    {
        await foreach (var chunk in audioInput.WithCancellation(cancellationToken))
        {
            if (chunk.Length == 0)
            {
                continue;
            }

            await conversation.SendAudioAsync(chunk, cancellationToken);
        }
    }

    private async Task PumpOutputAsync(
        RealtimeChatRunContext context,
        IRealtimeTurnStore turnStore,
        IRealtimeConversation conversation,
        IRealtimeConversationSink sink,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var turn = new AssistantTurn();

        // A provider response is currently generating (between response.created and response.done). Used only when
        // interruption is disabled, to decide whether a user utterance can be answered or was ignored by the model.
        var responseActive = false;

        // With barge-in off, every user utterance that starts while a response is active is rejected by the
        // provider ("active response in progress") and never answered. We record, in speech order, whether each
        // utterance was ignored so its lagging transcript can be dropped rather than shown as an unanswered prompt.
        // FIFO holds because both speech-started and transcript-completed arrive in utterance order.
        var ignoredUtterances = new Queue<bool>();

        await foreach (var evt in conversation.GetEventsAsync(cancellationToken))
        {
            switch (evt.Type)
            {
                case RealtimeConversationEventType.AssistantAudioDelta:
                    await sink.AssistantAudioAsync(sessionId, evt.Audio, cancellationToken);
                    break;

                case RealtimeConversationEventType.UserTranscript:
                    // With barge-in off, drop the transcript of an utterance that began while the model was already
                    // responding — it was never answered, so surfacing it would show a prompt the model ignored.
                    if (!context.AllowInterruption && ignoredUtterances.Count > 0 && ignoredUtterances.Dequeue())
                    {
                        break;
                    }

                    // The user's transcript for a turn can arrive AFTER the assistant has begun replying to it
                    // (input-audio transcription lags the model's spoken reply), so it must NOT end the assistant
                    // turn — doing so cut the reply and split it into two turns/bubbles. The assistant turn is
                    // flushed on its own done event, or on UserSpeechStarted for a genuine barge-in.
                    await PersistUserTurnAsync(context, turnStore, sessionId, evt.Text, cancellationToken);
                    await sink.UserTranscriptAsync(sessionId, evt.Text, cancellationToken);
                    break;

                case RealtimeConversationEventType.AssistantTranscriptDelta:
                    turn.MessageId ??= UniqueId.GenerateId();
                    turn.Builder.Append(evt.Text);
                    turn.HasContent = true;
                    await sink.AssistantTranscriptDeltaAsync(
                        sessionId, turn.MessageId, evt.Text, turn.MessageId, SnapshotReferences(), cancellationToken);
                    break;

                case RealtimeConversationEventType.AssistantTranscriptDone:
                    await FlushAssistantTurnAsync(context, turnStore, sink, sessionId, turn, finalText: evt.Text, cancellationToken);
                    break;

                case RealtimeConversationEventType.UserSpeechStarted:
                    // The provider raises speech-started whenever VAD hears the user, even when interruption is
                    // disabled (barge-in off only turns off interrupt_response, not the detector). Only treat it
                    // as a barge-in when interruption is allowed: persist whatever the assistant spoke before being
                    // cut off and let the sink flush the queued/playing audio. When barge-in is off the model keeps
                    // talking through the same response, so flushing here would end the turn early and split one
                    // reply into two bubbles.
                    if (context.AllowInterruption)
                    {
                        await FlushAssistantTurnAsync(context, turnStore, sink, sessionId, turn, finalText: null, cancellationToken);
                        await sink.SpeechStartedAsync(sessionId, cancellationToken);
                    }
                    else
                    {
                        // Barge-in off: this utterance will be answered only if no response is running right now;
                        // otherwise the provider rejects it and it must not be surfaced (see UserTranscript).
                        ignoredUtterances.Enqueue(responseActive);
                    }

                    break;

                case RealtimeConversationEventType.ResponseStarted:
                    responseActive = true;
                    break;

                case RealtimeConversationEventType.ResponseCompleted:
                    responseActive = false;

                    if (string.Equals(evt.ResponseStatus, RealtimeResponseStatus.Cancelled, StringComparison.OrdinalIgnoreCase))
                    {
                        await FlushAssistantTurnAsync(context, turnStore, sink, sessionId, turn, finalText: null, cancellationToken);
                    }

                    break;

                case RealtimeConversationEventType.Error:
                    if (IsBenignRealtimeError(evt.ErrorMessage))
                    {
                        // Expected races the user must never see as a chat bubble: with barge-in off the user
                        // speaking mid-reply makes the provider reject a second response ("active response in
                        // progress"); a barge-in cancel can arrive just after the response already ended ("no
                        // active response"). Neither is actionable — log it and keep the conversation going.
                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            _logger.LogDebug("Ignoring benign realtime error for session {SessionId}: {Message}", sessionId, evt.ErrorMessage);
                        }

                        break;
                    }

                    _logger.LogWarning("Realtime session error for session {SessionId}: {Message}", sessionId, evt.ErrorMessage);
                    await sink.ErrorAsync(evt.ErrorMessage ?? "An error occurred during the conversation.", cancellationToken);
                    break;
            }
        }

        // The stream ended (session closed). Persist any assistant turn that never received a done event.
        await FlushAssistantTurnAsync(context, turnStore, sink, sessionId, turn, finalText: null, cancellationToken);
    }

    private async Task PersistUserTurnAsync(
        RealtimeChatRunContext context,
        IRealtimeTurnStore turnStore,
        string sessionId,
        string text,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await turnStore.CreateUserTurnAsync(sessionId, text, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        if (context.OnUserUtteranceAsync is not null)
        {
            await context.OnUserUtteranceAsync(text, cancellationToken);
        }
    }

    private async Task FlushAssistantTurnAsync(
        RealtimeChatRunContext context,
        IRealtimeTurnStore turnStore,
        IRealtimeConversationSink sink,
        string sessionId,
        AssistantTurn turn,
        string? finalText,
        CancellationToken cancellationToken)
    {
        // Persist when deltas were accumulated, or when a final transcript arrived even without deltas
        // (some providers emit only the completed transcript).
        if (!turn.HasContent && string.IsNullOrWhiteSpace(finalText))
        {
            return;
        }

        var content = !string.IsNullOrWhiteSpace(finalText) ? finalText! : turn.Builder.ToString();
        var messageId = turn.MessageId ?? UniqueId.GenerateId();
        var references = SnapshotReferences();

        turn.Reset();

        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        await turnStore.CreateAssistantTurnAsync(
            sessionId, messageId, content, context.PromptTitle, references, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        await sink.AssistantCompletedAsync(sessionId, messageId, references, cancellationToken);

        if (context.OnAssistantCompletedAsync is not null)
        {
            await context.OnAssistantCompletedAsync(cancellationToken);
        }
    }

    private static Dictionary<string, AICompletionReference>? SnapshotReferences()
    {
        var references = AIInvocationScope.Current?.ToolReferences;

        if (references is null || references.Count == 0)
        {
            return null;
        }

        return new Dictionary<string, AICompletionReference>(references, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class AssistantTurn
    {
        public Utf16ValueStringBuilder Builder = ZString.CreateStringBuilder();

        public string? MessageId;

        public bool HasContent;

        public void Reset()
        {
            Builder.Dispose();
            Builder = ZString.CreateStringBuilder();
            MessageId = null;
            HasContent = false;
        }
    }
}
