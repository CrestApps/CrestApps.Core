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

        await foreach (var evt in conversation.GetEventsAsync(cancellationToken))
        {
            switch (evt.Type)
            {
                case RealtimeConversationEventType.AssistantAudioDelta:
                    await sink.AssistantAudioAsync(sessionId, evt.Audio, cancellationToken);
                    break;

                case RealtimeConversationEventType.UserTranscript:
                    // A new user utterance ends any assistant turn still in flight.
                    await FlushAssistantTurnAsync(context, turnStore, sink, sessionId, turn, finalText: null, cancellationToken);
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
                    // Barge-in: persist whatever the assistant actually spoke before being interrupted.
                    await FlushAssistantTurnAsync(context, turnStore, sink, sessionId, turn, finalText: null, cancellationToken);
                    await sink.SpeechStartedAsync(sessionId, cancellationToken);
                    break;

                case RealtimeConversationEventType.ResponseCompleted:
                    if (string.Equals(evt.ResponseStatus, RealtimeResponseStatus.Cancelled, StringComparison.OrdinalIgnoreCase))
                    {
                        await FlushAssistantTurnAsync(context, turnStore, sink, sessionId, turn, finalText: null, cancellationToken);
                    }

                    break;

                case RealtimeConversationEventType.Error:
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
