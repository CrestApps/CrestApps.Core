#nullable enable
using System.Diagnostics;
using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Drives one turn of a grounded realtime session: retrieve the knowledge for what the user just said, cover a
/// slow search with a spoken acknowledgement, then ask the model to answer.
/// </summary>
/// <remarks>
/// <para>
/// A grounded session is opened with the provider's automatic replies switched off, so it speaks only when asked.
/// That buys the guarantee that the model has the knowledge before it answers, and it takes on one obligation in
/// exchange: <b>every committed turn must end in a response request</b>. Failing to ask leaves the assistant mute
/// for the rest of the conversation — a far worse outcome than an ungrounded answer — so every path here,
/// including the failure paths, ends by asking. The watchdog is the backstop for the paths that do not exist yet.
/// </para>
/// <para>
/// Work runs off the event pump. The pump has to keep draining while retrieval is in flight, because the
/// acknowledgement's own lifecycle events arrive on it and the answer cannot be requested until that
/// acknowledgement has finished speaking — a provider rejects a second response while one is active.
/// </para>
/// </remarks>
internal sealed class GroundedTurnCoordinator : IAsyncDisposable
{
    /// <summary>
    /// What the model is told to say while the knowledge base is being searched. Deliberately prescriptive: an
    /// open-ended instruction invites the model to start answering the question it has not been given the material
    /// for yet.
    /// </summary>
    private const string AcknowledgementInstructions =
        "Say one short, natural filler sentence to let the user know you are looking up their question — for " +
        "example \"let me look that up\" or \"one moment while I check that\". Do not attempt to answer the " +
        "question, do not add any information, and do not ask anything. One sentence only.";

    // How long to wait for an acknowledgement to finish speaking before requesting the answer anyway. Reaching
    // this means the provider never reported the acknowledgement completing; answering over it is imperfect,
    // staying silent is broken.
    private static readonly TimeSpan _acknowledgementCompletionTimeout = TimeSpan.FromSeconds(10);

    private readonly IRealtimeConversation _conversation;
    private readonly string _sessionId;
    private readonly TimeSpan? _acknowledgementDelay;
    private readonly TimeSpan? _watchdogTimeout;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();

    private readonly Dictionary<string, CancellationTokenSource> _watchdogs = new(StringComparer.Ordinal);

    private CancellationTokenSource? _turnCancellation;
    private Task _turn = Task.CompletedTask;

    private TaskCompletionSource? _acknowledgementCompletion;
    private bool _expectAcknowledgementResponse;
    private string? _acknowledgementResponseId;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroundedTurnCoordinator"/> class.
    /// </summary>
    /// <param name="conversation">The live conversation.</param>
    /// <param name="sessionId">The session identifier, used in diagnostics.</param>
    /// <param name="acknowledgementDelay">How long retrieval may run before the wait is covered aloud, or <see langword="null"/> to never speak one.</param>
    /// <param name="watchdogTimeout">How long a committed turn may go unanswered before a reply is requested anyway, or <see langword="null"/> to disable the backstop.</param>
    /// <param name="logger">The logger.</param>
    public GroundedTurnCoordinator(
        IRealtimeConversation conversation,
        string sessionId,
        TimeSpan? acknowledgementDelay,
        TimeSpan? watchdogTimeout,
        ILogger logger)
    {
        _conversation = conversation;
        _sessionId = sessionId;
        _acknowledgementDelay = acknowledgementDelay;
        _watchdogTimeout = watchdogTimeout;
        _logger = logger;
    }

    /// <summary>
    /// Starts the backstop for a turn the provider has just committed. Cancelled as soon as the turn is resolved
    /// one way or the other; if it fires, the turn was swallowed somewhere and the model is asked to answer from
    /// the audio alone.
    /// </summary>
    /// <param name="itemId">The provider's conversation item id for the utterance.</param>
    /// <param name="cancellationToken">A token that ends the session.</param>
    public void TurnCommitted(string? itemId, CancellationToken cancellationToken)
    {
        if (_watchdogTimeout is not { } timeout)
        {
            return;
        }

        var key = WatchdogKey(itemId);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        lock (_gate)
        {
            if (_watchdogs.Remove(key, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }

            _watchdogs[key] = cancellation;
        }

        _ = WatchTurnAsync(key, timeout, cancellation.Token);
    }

    /// <summary>
    /// Runs retrieval for a transcribed utterance and asks the model to answer it.
    /// </summary>
    /// <param name="itemId">The provider's conversation item id for the utterance.</param>
    /// <param name="utterance">What the user said.</param>
    /// <param name="cancellationToken">A token that ends the session.</param>
    public void BeginTurn(string? itemId, string utterance, CancellationToken cancellationToken)
    {
        CancelWatchdog(itemId);

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource? previous;
        Task previousTurn;

        lock (_gate)
        {
            previous = _turnCancellation;
            previousTurn = _turn;
            _turnCancellation = cancellation;
        }

        previous?.Cancel();

        _turn = RunTurnAsync(previousTurn, utterance, cancellation.Token);
    }

    /// <summary>
    /// Drops a turn the provider refused to answer — the user talked over a reply with barge-in off. It owes no
    /// response, so its backstop is cancelled rather than allowed to request one for an utterance the model was
    /// never going to address.
    /// </summary>
    /// <param name="itemId">The provider's conversation item id for the utterance.</param>
    public void TurnIgnored(string? itemId)
    {
        CancelWatchdog(itemId);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Realtime session {SessionId}: a turn the provider ignored needs no answer, so its watchdog is cancelled.", _sessionId);
        }
    }

    /// <summary>
    /// Asks the model to answer a turn that could not be transcribed. There is nothing to retrieve against, but
    /// the model heard the audio, so it answers ungrounded rather than not at all.
    /// </summary>
    /// <param name="itemId">The provider's conversation item id for the utterance.</param>
    /// <param name="cancellationToken">A token that ends the session.</param>
    public void TurnTranscriptionFailed(string? itemId, CancellationToken cancellationToken)
    {
        CancelWatchdog(itemId);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Realtime session {SessionId}: no transcript for the turn, so it is answered without knowledge retrieval.", _sessionId);
        }

        Task previousTurn;

        lock (_gate)
        {
            previousTurn = _turn;
        }

        _turn = RequestAnswerAfterAsync(previousTurn, cancellationToken);
    }

    /// <summary>
    /// Abandons the turn currently being prepared because the user has started speaking again. Whatever they say
    /// next commits its own turn; answering the one they talked over would arrive late and off-topic.
    /// </summary>
    public void AbandonTurn()
    {
        CancellationTokenSource? cancellation;

        lock (_gate)
        {
            cancellation = _turnCancellation;
            _turnCancellation = null;
        }

        if (cancellation is null || cancellation.IsCancellationRequested)
        {
            return;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Realtime session {SessionId}: the user spoke again, so the turn being prepared is abandoned.", _sessionId);
        }

        cancellation.Cancel();
    }

    /// <summary>
    /// Records a response the provider has started, so an acknowledgement can be told apart from an answer.
    /// </summary>
    /// <param name="responseId">The provider's response id, when it supplies one.</param>
    public void ResponseStarted(string? responseId)
    {
        lock (_gate)
        {
            if (!_expectAcknowledgementResponse)
            {
                return;
            }

            _expectAcknowledgementResponse = false;
            _acknowledgementResponseId = responseId ?? string.Empty;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Realtime session {SessionId}: the acknowledgement is being spoken (response {ResponseId}).", _sessionId, responseId ?? "(none)");
        }
    }

    /// <summary>
    /// Releases a turn waiting for its acknowledgement to finish speaking.
    /// </summary>
    /// <param name="responseId">The provider's response id, when it supplies one.</param>
    public void ResponseCompleted(string? responseId)
    {
        TaskCompletionSource? completion = null;

        lock (_gate)
        {
            if (_acknowledgementResponseId is null)
            {
                return;
            }

            // An id-less provider event cannot be matched, so treat the next completion as the acknowledgement's:
            // waiting for one that can never be identified would stall the answer until the timeout.
            if (_acknowledgementResponseId.Length > 0 && !string.Equals(_acknowledgementResponseId, responseId, StringComparison.Ordinal))
            {
                return;
            }

            _acknowledgementResponseId = null;
            completion = _acknowledgementCompletion;
            _acknowledgementCompletion = null;
        }

        completion?.TrySetResult();
    }

    /// <summary>
    /// Gets whether a response is the spoken acknowledgement rather than an answer. Its audio plays, but it is
    /// not part of the conversation and must not be streamed or persisted as an assistant turn.
    /// </summary>
    /// <param name="responseId">The provider's response id, when it supplies one.</param>
    public bool IsAcknowledgement(string? responseId)
    {
        lock (_gate)
        {
            return _acknowledgementResponseId is not null
                && (_acknowledgementResponseId.Length == 0 || string.Equals(_acknowledgementResponseId, responseId, StringComparison.Ordinal));
        }
    }

    private async Task RunTurnAsync(Task previousTurn, string utterance, CancellationToken cancellationToken)
    {
        // Turns are strictly sequential: the previous one may still be waiting on its acknowledgement, and two
        // response requests in flight would collide.
        await SafeAwaitAsync(previousTurn);

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var retrieval = _conversation.GroundTurnAsync(utterance, cancellationToken);

        try
        {
            await SpeakAcknowledgementIfSlowAsync(retrieval, cancellationToken);

            var grounded = false;

            try
            {
                grounded = await retrieval;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Retrieval is an enhancement; a failure costs context, not the answer.
                _logger.LogError(ex, "Knowledge retrieval failed for realtime session {SessionId}. The model answers without it.", _sessionId);
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Realtime session {SessionId}: retrieval finished in {ElapsedMs} ms and {Outcome}.",
                    _sessionId, stopwatch.ElapsedMilliseconds, grounded ? "added context to the conversation" : "added nothing");
            }

            await WaitForAcknowledgementAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            await _conversation.RequestResponseAsync(cancellationToken);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Realtime session {SessionId}: answer requested {ElapsedMs} ms after the transcript arrived.",
                    _sessionId, stopwatch.ElapsedMilliseconds);
            }
        }
        catch (OperationCanceledException)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Realtime session {SessionId}: the turn was abandoned before it was answered.", _sessionId);
            }
        }
        catch (Exception ex)
        {
            // Nothing below this point can recover the turn, and the session answers only when asked, so make one
            // last attempt rather than leaving it mute.
            _logger.LogError(ex, "Realtime session {SessionId}: preparing the turn failed. Requesting an answer anyway.", _sessionId);

            await TryRequestResponseAsync(CancellationToken.None);
        }
    }

    private async Task SpeakAcknowledgementIfSlowAsync(Task retrieval, CancellationToken cancellationToken)
    {
        if (_acknowledgementDelay is not { } delay)
        {
            return;
        }

        var timer = Task.Delay(delay, cancellationToken);

        if (await Task.WhenAny(retrieval, timer) != timer || retrieval.IsCompleted)
        {
            // The search beat the deadline, so the user hears the answer without a filler in front of it.
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            _acknowledgementCompletion = completion;
            _expectAcknowledgementResponse = true;
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            // Worth Information: this is the user-visible symptom of a slow index, and the line that explains why
            // a conversation says "let me look that up" on some turns and not others.
            _logger.LogInformation(
                "Realtime session {SessionId}: knowledge retrieval is still running after {DelayMs} ms, so the wait is covered with a spoken acknowledgement.",
                _sessionId, (int)delay.TotalMilliseconds);
        }

        try
        {
            await _conversation.RequestAcknowledgementAsync(AcknowledgementInstructions, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The filler is cosmetic. Losing it must not cost the answer, so clear the wait it would have imposed.
            _logger.LogWarning(ex, "Realtime session {SessionId}: the spoken acknowledgement could not be requested.", _sessionId);

            lock (_gate)
            {
                _acknowledgementCompletion = null;
                _expectAcknowledgementResponse = false;
                _acknowledgementResponseId = null;
            }

            completion.TrySetResult();
        }
    }

    private async Task WaitForAcknowledgementAsync(CancellationToken cancellationToken)
    {
        Task? pending;

        lock (_gate)
        {
            pending = _acknowledgementCompletion?.Task;
        }

        if (pending is null)
        {
            return;
        }

        try
        {
            await pending.WaitAsync(_acknowledgementCompletionTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Realtime session {SessionId}: the spoken acknowledgement never reported completing after {TimeoutSeconds}s. Answering anyway.",
                _sessionId, (int)_acknowledgementCompletionTimeout.TotalSeconds);

            lock (_gate)
            {
                _acknowledgementCompletion = null;
                _acknowledgementResponseId = null;
            }
        }
    }

    private async Task WatchTurnAsync(string key, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(timeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The turn resolved normally, which is the common case.
            return;
        }
        finally
        {
            DisposeWatchdog(key);
        }

        // Warning, not debug: reaching here means a turn was swallowed. The session recovers, but something
        // upstream is not behaving and it will keep happening.
        _logger.LogWarning(
            "Realtime session {SessionId}: a committed turn produced no transcript within {TimeoutSeconds}s. Requesting an answer so the session does not go silent.",
            _sessionId, (int)timeout.TotalSeconds);

        await TryRequestResponseAsync(CancellationToken.None);
    }

    private async Task RequestAnswerAfterAsync(Task previousTurn, CancellationToken cancellationToken)
    {
        await SafeAwaitAsync(previousTurn);

        try
        {
            await WaitForAcknowledgementAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            await _conversation.RequestResponseAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Realtime session {SessionId}: could not request an answer for an untranscribed turn.", _sessionId);
        }
    }

    private async Task TryRequestResponseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _conversation.RequestResponseAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Realtime session {SessionId}: the fallback response request failed. The session may not reply to this turn.", _sessionId);
        }
    }

    private void CancelWatchdog(string? itemId)
    {
        DisposeWatchdog(WatchdogKey(itemId), cancel: true);
    }

    private void DisposeWatchdog(string key, bool cancel = false)
    {
        CancellationTokenSource? cancellation;

        lock (_gate)
        {
            if (!_watchdogs.Remove(key, out cancellation))
            {
                return;
            }
        }

        if (cancel)
        {
            cancellation.Cancel();
        }

        cancellation.Dispose();
    }

    // Providers do not always supply an item id. One unnamed turn at a time is the realistic case, so they share
    // a key rather than each starting a watchdog nothing can ever cancel.
    private static string WatchdogKey(string? itemId)
        => string.IsNullOrEmpty(itemId) ? " unnamed" : itemId;

    private static async Task SafeAwaitAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception)
        {
            // The previous turn reports its own failures; this await only orders them.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource[] watchdogs;
        CancellationTokenSource? turnCancellation;
        Task turn;

        lock (_gate)
        {
            watchdogs = [.. _watchdogs.Values];
            _watchdogs.Clear();
            turnCancellation = _turnCancellation;
            _turnCancellation = null;
            turn = _turn;
        }

        foreach (var watchdog in watchdogs)
        {
            watchdog.Cancel();
            watchdog.Dispose();
        }

        turnCancellation?.Cancel();

        await SafeAwaitAsync(turn);

        turnCancellation?.Dispose();
    }
}
