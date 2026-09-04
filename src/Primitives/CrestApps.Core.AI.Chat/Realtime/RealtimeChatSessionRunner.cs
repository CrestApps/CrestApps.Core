#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only.
#nullable enable
using System.Buffers;
using System.Diagnostics;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Realtime;
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
    // The realtime pipeline's audio format.
    private const int SampleRate = 24000;

    // Smallest amount of microphone audio worth sending as one provider message. The WebRTC transport delivers
    // 20 ms frames, which would otherwise become 50 JSON+Base64 messages a second per session; batching them keeps
    // the message rate at ~10/s without adding latency the user can hear.
    private const int MinimumInputBatchMs = 100;

    private const int MinimumInputBatchBytes = MinimumInputBatchMs * SampleRate / 1000 * 2;

    // PCM16 mono at the pipeline's sample rate: two bytes per sample.
    private const int BytesPerMillisecond = SampleRate / 1000 * 2;

    // Roughly how much audio the browser holds in its own jitter buffer beyond what the transport reports, so a
    // truncation errs on the side of claiming the user heard slightly less rather than slightly more.
    private const int ClientJitterBufferMs = 80;

    // Longest the half-duplex microphone gate will be held shut waiting for queued assistant audio to drain.
    // The queue can run tens of seconds deep when the provider generates a long reply far faster than real time,
    // and holding the gate for all of it means the user presses on, speaks, and is simply not heard for half a
    // minute with no indication why. Past this point, letting them interrupt a still-playing reply is the lesser
    // of the two failures.
    private const int MaxHalfDuplexHoldMs = 2000;

    // A microphone batch (~100 ms) that the provider takes this long to accept is a stall worth logging.
    private const long SlowProviderSendMs = 1000;

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

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Realtime session {SessionId} starting (voice='{Voice}', allowInterruption={AllowInterruption}, deployment='{Deployment}').",
                context.SessionId, context.Voice ?? "(default)", context.AllowInterruption, context.RealtimeDeploymentName ?? "(default)");
        }

        var endReason = RealtimeSessionEndReasons.Completed;
        var idleTimedOut = false;

        try
        {
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

            // The provider session is open: tell the client it can stop showing "connecting" and start listening.
            await sink.SessionReadyAsync(context.SessionId, cancellationToken);

            // Hand the host a control so settings changed mid-conversation reach both the input pump and the
            // provider, instead of only taking effect on the user's next session.
            context.OnSessionStarted?.Invoke(new RealtimeSessionControl(conversation, context));

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Shared between the two pumps: whether the model is currently generating a response. With barge-in off it
            // enforces half-duplex — the input pump drops the user's mic audio while a response is active so a follow-up
            // spoken over the assistant never reaches the provider (and so is never processed or answered).
            var responseState = new ResponseActivity();

            // Reset by every user utterance; the idle watchdog reads it to decide whether anyone is still here.
            var activity = new SessionActivity(_timeProvider.GetUtcNow());

            var inbound = PumpInputAsync(conversation, audioInput, context, responseState, linkedCts.Token);
            var outbound = PumpOutputAsync(context, turnStore, conversation, sink, context.SessionId, responseState, activity, linkedCts.Token);
            var idle = WatchForIdleAsync(context, activity, linkedCts.Token);

            var finished = await Task.WhenAny(inbound, outbound, idle);

            if (finished == idle && idle.IsCompletedSuccessfully && idle.Result)
            {
                idleTimedOut = true;
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Realtime session {SessionId} ended after {Minutes:0.#} minutes without user speech.",
                        context.SessionId, context.IdleTimeout!.Value.TotalMinutes);
                }
            }

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
        catch (Exception ex)
        {
            endReason = ex is OperationCanceledException
                ? RealtimeSessionEndReasons.Cancelled
                : RealtimeSessionEndReasons.Error;

            throw;
        }
        finally
        {
            if (idleTimedOut)
            {
                // An idle close is deliberate, so say so rather than reporting the cancellation it is implemented
                // with — the client offers to resume instead of showing a failure.
                endReason = RealtimeSessionEndReasons.Idle;
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                endReason = RealtimeSessionEndReasons.Cancelled;
            }

            // Always tell the client the session is over — provider close, cap, error, or a normal stop. Without
            // this the browser keeps the microphone open and streams audio into a session that no longer exists.
            // Deliberately not passing the (likely already cancelled) session token: this is the last message.
            try
            {
                await sink.SessionEndedAsync(context.SessionId, endReason, CancellationToken.None);
            }
            catch (Exception notifyEx)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(notifyEx, "Failed to notify the client that realtime session {SessionId} ended.", context.SessionId);
                }
            }
        }
    }

    // Transport aborts that are expected while a realtime session is shutting down (for example when the user
    // stops the conversation): cancellation, or the underlying socket being aborted mid read/write.
    private static bool IsExpectedShutdownException(Exception ex)
        => ex is OperationCanceledException
            or IOException
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
        // "Audio content of Xms is already shorter than Yms" / "audio_end_ms" — a barge-in truncation that named
        // more audio than the interrupted item holds; the reply was already over, nothing to trim.
        return message.Contains("active response in progress", StringComparison.OrdinalIgnoreCase)
            || message.Contains("no active response", StringComparison.OrdinalIgnoreCase)
            || message.Contains("already shorter", StringComparison.OrdinalIgnoreCase)
            || message.Contains("audio_end_ms", StringComparison.OrdinalIgnoreCase);
    }

    // The provider refused the turn-detection configuration (typically a deployment that does not support
    // semantic_vad). The conversation can continue on plain server VAD instead of failing.
    private static bool IsTurnDetectionRejection(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("semantic_vad", StringComparison.OrdinalIgnoreCase)
            || message.Contains("turn_detection", StringComparison.OrdinalIgnoreCase)
            || message.Contains("eagerness", StringComparison.OrdinalIgnoreCase);
    }

    // Takes the run context rather than a captured flag: barge-in can be toggled mid-conversation (see
    // RealtimeSessionControl), and the pump has to honour the change on the very next microphone frame.
    private async Task PumpInputAsync(
        IRealtimeConversation conversation,
        IAsyncEnumerable<ReadOnlyMemory<byte>> audioInput,
        RealtimeChatRunContext context,
        ResponseActivity responseState,
        CancellationToken cancellationToken)
    {
        var batch = new ArrayBufferWriter<byte>(MinimumInputBatchBytes * 2);
        var sendClock = new Stopwatch();

        // A send to the provider that takes longer than the audio it carries is the provider (or the network) not
        // accepting input. The transport buffers ~2 s behind this loop and then drops the oldest audio, so a stall
        // here is what the user experiences as "it did not hear me" — say how long it lasted.
        async Task SendAsync(ReadOnlyMemory<byte> audio)
        {
            sendClock.Restart();
            await conversation.SendAudioAsync(audio, cancellationToken);
            var elapsedMs = sendClock.ElapsedMilliseconds;
            if (elapsedMs >= SlowProviderSendMs)
            {
                _logger.LogWarning(
                    "Realtime session {SessionId}: the provider accepted {AudioMs} ms of microphone audio only after {ElapsedMs} ms; audio behind it is being delayed or dropped.",
                    context.SessionId, audio.Length / 2 * 1000 / SampleRate, elapsedMs);
            }
        }

        await foreach (var chunk in audioInput.WithCancellation(cancellationToken))
        {
            if (chunk.Length == 0)
            {
                continue;
            }

            // Half-duplex when interruption is disabled: while the model is answering, discard the user's mic audio
            // instead of forwarding it. The provider never hears a follow-up spoken over the assistant, so it cannot
            // start (and we never surface) a second overlapping response. When interruption is on, always forward —
            // the provider needs the audio to detect a barge-in.
            if (!context.AllowInterruption && responseState.Active)
            {
                // Drop whatever was mid-batch too: it is the tail of speech we have decided not to forward.
                batch.ResetWrittenCount();

                continue;
            }

            // Transports that already deliver large frames (the SignalR path sends ~170 ms) pass straight through.
            if (batch.WrittenCount == 0 && chunk.Length >= MinimumInputBatchBytes)
            {
                await SendAsync(chunk);

                continue;
            }

            batch.Write(chunk.Span);

            if (batch.WrittenCount >= MinimumInputBatchBytes)
            {
                await SendAsync(batch.WrittenMemory.ToArray());
                batch.ResetWrittenCount();
            }
        }

        if (batch.WrittenCount > 0)
        {
            await SendAsync(batch.WrittenMemory.ToArray());
        }
    }

    private async Task PumpOutputAsync(
        RealtimeChatRunContext context,
        IRealtimeTurnStore turnStore,
        IRealtimeConversation conversation,
        IRealtimeConversationSink sink,
        string sessionId,
        ResponseActivity responseState,
        SessionActivity activity,
        CancellationToken cancellationToken)
    {
        var turn = new AssistantTurn();

        // User utterances that have been committed by the provider but not yet transcribed. Keyed by the
        // provider's item id, which is the only thing that reliably pairs an utterance with its transcript:
        // transcription lags the spoken reply, can fail outright, and (with barge-in off) some utterances are
        // never answered at all. Arrival order alone drifted by one turn the first time any of those happened,
        // which silently removed an answered prompt from the conversation.
        var pendingTurns = new PendingRealtimeTurns();

        // What the user has actually heard of the assistant's current item, so an interruption can tell the
        // provider where the reply really stopped.
        var playback = new AssistantPlaybackTracker();

        // Diagnostics for a provider whose events this build does not recognise (see the warning at the end).
        var sawSpeechStarted = false;
        var sawAnyResponse = false;

        // Whether the session has already been switched to server VAD after the provider rejected semantic turn
        // detection; one fallback per session, never a loop.
        var turnDetectionFallbackDone = false;

        await foreach (var evt in conversation.GetEventsAsync(cancellationToken))
        {
            switch (evt.Type)
            {
                case RealtimeConversationEventType.AssistantAudioDelta:
                    playback.Append(evt.ItemId, evt.Audio.Length);
                    await sink.AssistantAudioAsync(sessionId, evt.Audio, cancellationToken);
                    break;

                case RealtimeConversationEventType.UserTurnCommitted:
                    {
                        activity.Touch(_timeProvider.GetUtcNow());

                        // The provider decides at commit time whether this utterance gets a response, so this — not
                        // speech-started — is where "will it be answered?" can be judged. An utterance that began
                        // during a reply but committed after it finished IS answered, and used to have its
                        // transcript thrown away.
                        var committed = pendingTurns.Add(evt.ItemId, ignored: !context.AllowInterruption && responseState.Active);

                        // Create the turn now, while it is genuinely earlier than the reply it prompts. Creating it
                        // when the transcript arrives stamped it after the assistant's answer, so history reloaded
                        // with the prompt underneath its own reply.
                        await turnStore.CreateUserTurnAsync(sessionId, committed, string.Empty, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
                        await sink.UserTurnPendingAsync(sessionId, committed, cancellationToken);
                    }

                    break;

                case RealtimeConversationEventType.UserTranscriptFailed:
                    {
                        var failed = pendingTurns.Resolve(evt.ItemId);

                        if (failed is not null)
                        {
                            await turnStore.DeleteUserTurnAsync(sessionId, failed.TurnId, cancellationToken);
                            await sink.UserTurnDroppedAsync(sessionId, failed.TurnId, cancellationToken);
                        }

                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            _logger.LogDebug(
                                "Input-audio transcription failed for session {SessionId}: {Message}", sessionId, evt.ErrorMessage ?? "(no detail)");
                        }
                    }

                    break;

                case RealtimeConversationEventType.UserTranscript:
                    {
                        var resolved = pendingTurns.Resolve(evt.ItemId);

                        // With barge-in off, drop an utterance the provider refused to answer — surfacing it would
                        // show a prompt the model ignored.
                        if (resolved is { Ignored: true })
                        {
                            await turnStore.DeleteUserTurnAsync(sessionId, resolved.TurnId, cancellationToken);
                            await sink.UserTurnDroppedAsync(sessionId, resolved.TurnId, cancellationToken);

                            break;
                        }

                        // The user's transcript for a turn can arrive AFTER the assistant has begun replying to it
                        // (input-audio transcription lags the model's spoken reply), so it must NOT end the assistant
                        // turn — doing so cut the reply and split it into two turns/bubbles. The assistant turn is
                        // flushed on its own done event, or on UserSpeechStarted for a genuine barge-in.
                        var turnId = resolved?.TurnId;

                        if (turnId is null)
                        {
                            // A provider that never announced the commit: fall back to creating the turn now.
                            turnId = UniqueId.GenerateId();
                            await turnStore.CreateUserTurnAsync(sessionId, turnId, evt.Text, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
                        }
                        else
                        {
                            await turnStore.UpdateUserTurnAsync(sessionId, turnId, evt.Text, cancellationToken);
                        }

                        await sink.UserTranscriptAsync(sessionId, turnId, evt.Text, cancellationToken);
                        await NotifyUserUtteranceAsync(context, evt.Text, cancellationToken);
                    }

                    break;

                case RealtimeConversationEventType.AssistantTranscriptDelta:
                    turn.MessageId ??= UniqueId.GenerateId();
                    turn.Builder.Append(evt.Text);
                    turn.HasContent = true;
                    await sink.AssistantTranscriptDeltaAsync(
                        sessionId, turn.MessageId, evt.Text, turn.MessageId, SnapshotReferences(), cancellationToken);
                    break;

                case RealtimeConversationEventType.AssistantTranscriptDone:
                    playback.Reset();
                    await FlushAssistantTurnAsync(context, turnStore, sink, sessionId, turn, finalText: evt.Text, cancellationToken);
                    break;

                case RealtimeConversationEventType.UserSpeechStarted:
                    // The provider raises speech-started whenever VAD hears the user, even when interruption is
                    // disabled (barge-in off only turns off interrupt_response, not the detector). Only treat it
                    // as a barge-in when interruption is allowed: persist whatever the assistant spoke before being
                    // cut off and let the sink flush the queued/playing audio. When barge-in is off the model keeps
                    // talking through the same response, so flushing here would end the turn early and split one
                    // reply into two bubbles.
                    sawSpeechStarted = true;
                    activity.Touch(_timeProvider.GetUtcNow());

                    if (context.AllowInterruption)
                    {
                        if (responseState.Active && _logger.IsEnabled(LogLevel.Information))
                        {
                            // Support diagnostic: a reply that "stumbles and then continues" is one that was cut
                            // off here and re-generated. If this fires when nobody spoke, the microphone gate let
                            // echo or room noise through and this line is the evidence.
                            _logger.LogInformation(
                                "Realtime session {SessionId}: the provider heard the user while a reply was playing; the reply is interrupted after {HeardMs} ms of audio.",
                                sessionId, Math.Max(0, playback.SentMs - sink.PendingPlaybackMs));
                        }

                        await TruncateInterruptedAssistantItemAsync(conversation, sink, playback, sessionId, cancellationToken);
                        await FlushAssistantTurnAsync(context, turnStore, sink, sessionId, turn, finalText: null, cancellationToken);
                        await sink.SpeechStartedAsync(sessionId, cancellationToken);
                    }

                    break;

                case RealtimeConversationEventType.ResponseStarted:
                    // With barge-in off the user can speak a follow-up once the previous reply's text is done, while
                    // its paced audio is still draining. When that follow-up's response starts, drop the old audio so
                    // the newest reply plays instead of the stale one finishing first. (No-op for the first response
                    // of a turn and when nothing is buffered.)
                    if (!context.AllowInterruption)
                    {
                        await sink.FlushPlaybackAsync(sessionId, cancellationToken);
                    }

                    responseState.Activate();
                    sawAnyResponse = true;
                    playback.Reset();
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("Realtime response started for session {SessionId} (response {ResponseId}).", sessionId, evt.ResponseId ?? "(none)");
                    }

                    break;

                case RealtimeConversationEventType.ResponseCompleted:
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("Realtime response completed for session {SessionId} (status={Status}).", sessionId, evt.ResponseStatus ?? "(none)");
                    }

                    // "Done" from the provider means it stopped generating, not that the user has heard the reply:
                    // on a paced transport seconds of audio can still be queued. Reopening the half-duplex mic gate
                    // now would let the tail of the assistant's own voice back into the provider, so hold the gate
                    // closed until the queued audio has actually drained.
                    await ClearResponseActivityWhenDrainedAsync(responseState, sink, sessionId, context.AllowInterruption, cancellationToken);

                    // Persist whatever was spoken regardless of how the response ended. A response that ends
                    // "failed" or "incomplete" (rate limit, content filter, token cap) still produced text; leaving
                    // it un-flushed made the next reply's deltas append to the same bubble.
                    await FlushAssistantTurnAsync(context, turnStore, sink, sessionId, turn, finalText: null, cancellationToken);

                    // A failed/incomplete response is a real problem the user should see, unlike the benign
                    // turn-taking races filtered below.
                    if (!string.IsNullOrWhiteSpace(evt.ErrorMessage) &&
                        !string.Equals(evt.ResponseStatus, RealtimeResponseStatus.Cancelled, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning(
                            "Realtime response for session {SessionId} ended with status '{Status}': {Message}", sessionId, evt.ResponseStatus ?? "(none)", evt.ErrorMessage);
                        await sink.ErrorAsync(evt.ErrorMessage, cancellationToken);
                    }

                    break;

                case RealtimeConversationEventType.Error:
                    if (!turnDetectionFallbackDone && IsTurnDetectionRejection(evt.ErrorMessage))
                    {
                        // A deployment that does not support semantic turn detection rejects the session
                        // configuration. Switch to server VAD in place rather than showing the user an error for a
                        // conversation that can perfectly well continue.
                        turnDetectionFallbackDone = true;
                        _logger.LogWarning(
                            "Realtime session {SessionId}: the provider rejected the turn-detection configuration ({Message}); falling back to server VAD.",
                            sessionId, evt.ErrorMessage);

                        try
                        {
                            await conversation.UpdateTurnDetectionAsync(
                                context.AllowInterruption, context.SilenceDurationMs, context.VadThreshold,
                                RealtimeTurnDetectionTypes.ServerVad, cancellationToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogWarning(ex, "Realtime session {SessionId}: could not switch to server VAD.", sessionId);
                        }

                        break;
                    }

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

        // A session that held a whole conversation without the provider ever reporting speech-started means this
        // deployment's events are not being mapped. Barge-in flush, partial-turn persistence and the half-duplex
        // bookkeeping all depend on that event, and all of them fail silently without it — so say it once, rather
        // than leaving an operator to work out why interruptions do nothing.
        if (!sawSpeechStarted && sawAnyResponse)
        {
            _logger.LogWarning(
                "Realtime session {SessionId} ran without the provider ever reporting user speech. Barge-in and turn " +
                "bookkeeping cannot work for this deployment: its events are not recognised by the event mapper.",
                sessionId);
        }

        // The stream ended (session closed). Persist any assistant turn that never received a done event.
        await FlushAssistantTurnAsync(context, turnStore, sink, sessionId, turn, finalText: null, cancellationToken);
    }

    // Ends the session when nobody has spoken for the configured idle window. Returns true when it fired, false
    // when the session ended for another reason first. A realtime session holds an open (billed) provider
    // connection whether or not anyone is talking, so a forgotten tab should not keep one alive until the
    // provider's own hour-long cap closes it.
    private async Task<bool> WatchForIdleAsync(
        RealtimeChatRunContext context,
        SessionActivity activity,
        CancellationToken cancellationToken)
    {
        if (context.IdleTimeout is not { } timeout || timeout <= TimeSpan.Zero)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return false;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var idleFor = _timeProvider.GetUtcNow() - activity.LastUserSpeechUtc;
            var remaining = timeout - idleFor;

            if (remaining <= TimeSpan.Zero)
            {
                return true;
            }

            // Wake when the current window would expire; a later utterance simply pushes the deadline out.
            await Task.Delay(remaining, _timeProvider, cancellationToken);
        }

        return false;
    }

    // Marks the response finished for half-duplex purposes. When the transport still holds paced audio the user has
    // not heard yet, the mic gate stays closed for that long first; a later response reopening the gate itself
    // (Activate) supersedes the pending clear, so a fast follow-up is never blocked by a stale timer.
    private Task ClearResponseActivityWhenDrainedAsync(
        ResponseActivity responseState,
        IRealtimeConversationSink sink,
        string sessionId,
        bool allowInterruption,
        CancellationToken cancellationToken)
    {
        // Only the half-duplex path reads this flag; with interruption on the microphone stays open regardless,
        // so there is nothing to hold and no reason to schedule a timer.
        var queuedMs = allowInterruption ? 0 : sink.PendingPlaybackMs;
        var pendingMs = Math.Min(queuedMs, MaxHalfDuplexHoldMs);

        if (queuedMs > MaxHalfDuplexHoldMs)
        {
            _logger.LogWarning(
                "Realtime session {SessionId}: {QueuedMs} ms of assistant audio is still queued; holding the " +
                "half-duplex microphone gate for {HoldMs} ms only, so the user is not left unheard for the rest of it.",
                sessionId, queuedMs, MaxHalfDuplexHoldMs);
        }

        if (pendingMs <= 0)
        {
            responseState.Active = false;

            return Task.CompletedTask;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Realtime session {SessionId}: holding the half-duplex gate for {PendingMs} ms of undrained assistant audio.", sessionId, pendingMs);
        }

        var generation = responseState.Generation;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(pendingMs), _timeProvider, cancellationToken);
                responseState.DeactivateIfCurrent(generation);
            }
            catch (OperationCanceledException)
            {
                // The session is tearing down; nothing left to gate.
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    // Tells the provider how much of an interrupted reply the user actually heard, so the rest is removed from the
    // model's context. Without it the model believes it delivered the whole answer and "what did you just say?"
    // reflects text the user never heard.
    private async Task TruncateInterruptedAssistantItemAsync(
        IRealtimeConversation conversation,
        IRealtimeConversationSink sink,
        AssistantPlaybackTracker playback,
        string sessionId,
        CancellationToken cancellationToken)
    {
        // What was handed to the transport, minus what it is still holding, minus the browser's own jitter buffer.
        // Transports that hand audio straight to the client report nothing pending, so this becomes "everything
        // sent" — an over-estimate that trims nothing rather than trimming too much.
        var heardMs = playback.SentMs - sink.PendingPlaybackMs - ClientJitterBufferMs;

        if (playback.ItemId is null || heardMs <= 0)
        {
            return;
        }

        try
        {
            await conversation.TruncateAssistantAudioAsync(playback.ItemId, heardMs, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best effort: a provider that does not accept the message must not end the conversation.
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(ex, "Could not truncate the interrupted assistant item for session {SessionId}.", sessionId);
            }
        }

        playback.Reset();
    }

    private static Task NotifyUserUtteranceAsync(RealtimeChatRunContext context, string text, CancellationToken cancellationToken)
    {
        if (context.OnUserUtteranceAsync is null || string.IsNullOrWhiteSpace(text))
        {
            return Task.CompletedTask;
        }

        return context.OnUserUtteranceAsync(text, cancellationToken);
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

    // Tracks whether the model is currently generating a response, shared across the input and output pumps
    // (different threads), so volatile guarantees the input pump sees the flip promptly. The generation counter
    // lets a delayed "drained" clear be discarded once a newer response has already taken over.
    private sealed class ResponseActivity
    {
        private volatile bool _active;
        private int _generation;

        public bool Active
        {
            get => _active;
            set => _active = value;
        }

        public int Generation => Volatile.Read(ref _generation);

        public void Activate()
        {
            Interlocked.Increment(ref _generation);
            _active = true;
        }

        public void DeactivateIfCurrent(int generation)
        {
            if (Volatile.Read(ref _generation) == generation)
            {
                _active = false;
            }
        }
    }

    // Pairs committed user utterances with the transcripts that arrive for them later. Keyed on the provider's
    // item id where there is one; providers that do not supply ids fall back to arrival order, which is what the
    // runner did before item ids existed.
    private sealed class PendingRealtimeTurns
    {
        private readonly Dictionary<string, PendingTurn> _byItemId = new(StringComparer.Ordinal);
        private readonly Queue<PendingTurn> _unkeyed = new();

        public string Add(string? itemId, bool ignored)
        {
            var turn = new PendingTurn(UniqueId.GenerateId(), ignored);

            if (string.IsNullOrEmpty(itemId))
            {
                _unkeyed.Enqueue(turn);
            }
            else
            {
                _byItemId[itemId] = turn;
            }

            return turn.TurnId;
        }

        public PendingTurn? Resolve(string? itemId)
        {
            if (!string.IsNullOrEmpty(itemId) && _byItemId.Remove(itemId, out var keyed))
            {
                return keyed;
            }

            return _unkeyed.TryDequeue(out var unkeyed) ? unkeyed : null;
        }
    }

    private sealed record PendingTurn(string TurnId, bool Ignored);

    // When the user was last heard, shared between the output pump and the idle watchdog.
    private sealed class SessionActivity
    {
        private long _lastUserSpeechTicks;

        public SessionActivity(DateTimeOffset startedUtc)
        {
            _lastUserSpeechTicks = startedUtc.UtcTicks;
        }

        public DateTimeOffset LastUserSpeechUtc => new(Volatile.Read(ref _lastUserSpeechTicks), TimeSpan.Zero);

        public void Touch(DateTimeOffset nowUtc)
            => Volatile.Write(ref _lastUserSpeechTicks, nowUtc.UtcTicks);
    }

    // Counts the assistant audio handed to the transport for the item currently being spoken.
    private sealed class AssistantPlaybackTracker
    {
        public string? ItemId { get; private set; }

        public int SentMs { get; private set; }

        public void Append(string? itemId, int byteCount)
        {
            if (itemId is not null && !string.Equals(itemId, ItemId, StringComparison.Ordinal))
            {
                ItemId = itemId;
                SentMs = 0;
            }

            SentMs += byteCount / BytesPerMillisecond;
        }

        public void Reset()
        {
            ItemId = null;
            SentMs = 0;
        }
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
