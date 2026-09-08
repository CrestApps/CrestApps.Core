#nullable enable
using CrestApps.Core.AI.Chat.Realtime;
using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Realtime;

/// <summary>
/// Covers the obligation a grounded session takes on: it answers only when the server asks it to, so every
/// committed turn must end in a response request — including the turns that go wrong.
/// </summary>
public sealed class GroundedTurnCoordinatorTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task BeginTurn_WithFastRetrieval_GroundsThenAnswersWithoutSpeakingAnAcknowledgement()
    {
        var conversation = new ControllableConversation();
        await using var coordinator = CreateCoordinator(conversation, acknowledgementDelay: TimeSpan.FromSeconds(30));

        coordinator.BeginTurn("item-1", "what is the refund policy", CancellationToken.None);

        await WaitForAsync(() => conversation.ResponseRequests == 1);

        Assert.Equal(["what is the refund policy"], conversation.GroundedUtterances);

        // The search beat the deadline, so the user hears the answer with no filler in front of it.
        Assert.Empty(conversation.Acknowledgements);
    }

    [Fact]
    public async Task BeginTurn_WithSlowRetrieval_SpeaksAnAcknowledgementAndWaitsForItBeforeAnswering()
    {
        var conversation = new ControllableConversation { RetrievalGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var coordinator = CreateCoordinator(conversation, acknowledgementDelay: TimeSpan.FromMilliseconds(20));

        coordinator.BeginTurn("item-1", "what is the refund policy", CancellationToken.None);

        await WaitForAsync(() => conversation.Acknowledgements.Count == 1);

        // The acknowledgement is speaking; the provider rejects a second response while one is active.
        coordinator.ResponseStarted("ack-1");
        Assert.True(coordinator.IsAcknowledgement("ack-1"));

        conversation.RetrievalGate!.SetResult(true);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(0, conversation.ResponseRequests);

        coordinator.ResponseCompleted("ack-1");

        await WaitForAsync(() => conversation.ResponseRequests == 1);
        Assert.False(coordinator.IsAcknowledgement("ack-1"));
    }

    [Fact]
    public async Task BeginTurn_WhenRetrievalThrows_StillAnswers()
    {
        var conversation = new ControllableConversation { RetrievalFailure = new InvalidOperationException("index down") };
        await using var coordinator = CreateCoordinator(conversation);

        coordinator.BeginTurn("item-1", "what is the refund policy", CancellationToken.None);

        await WaitForAsync(() => conversation.ResponseRequests == 1);
    }

    [Fact]
    public async Task TurnTranscriptionFailed_AnswersWithoutRetrieving()
    {
        var conversation = new ControllableConversation();
        await using var coordinator = CreateCoordinator(conversation);

        coordinator.TurnCommitted("item-1", CancellationToken.None);
        coordinator.TurnTranscriptionFailed("item-1", CancellationToken.None);

        await WaitForAsync(() => conversation.ResponseRequests == 1);
        Assert.Empty(conversation.GroundedUtterances);
    }

    [Fact]
    public async Task TurnIgnored_AnswersNothing()
    {
        // The provider refused the utterance because the user talked over a reply with barge-in off. Answering it
        // would surface a reply to a prompt the model was never going to address.
        var conversation = new ControllableConversation();
        await using var coordinator = CreateCoordinator(conversation, watchdog: TimeSpan.FromMilliseconds(100));

        coordinator.TurnCommitted("item-1", CancellationToken.None);
        coordinator.TurnIgnored("item-1");

        await Task.Delay(400, TestContext.Current.CancellationToken);

        Assert.Equal(0, conversation.ResponseRequests);
    }

    [Fact]
    public async Task Watchdog_WhenATurnIsSwallowed_RequestsAnAnswerAnyway()
    {
        // Neither a transcript nor a failure ever arrives. Without the backstop the assistant would stay mute for
        // the rest of the conversation, which is worse than an ungrounded answer.
        var conversation = new ControllableConversation();
        await using var coordinator = CreateCoordinator(conversation, watchdog: TimeSpan.FromMilliseconds(100));

        coordinator.TurnCommitted("item-1", CancellationToken.None);

        await WaitForAsync(() => conversation.ResponseRequests == 1);
        Assert.Empty(conversation.GroundedUtterances);
    }

    [Fact]
    public async Task Watchdog_WhenTheTranscriptArrives_DoesNotAlsoFire()
    {
        var conversation = new ControllableConversation();
        await using var coordinator = CreateCoordinator(conversation, watchdog: TimeSpan.FromMilliseconds(150));

        coordinator.TurnCommitted("item-1", CancellationToken.None);
        coordinator.BeginTurn("item-1", "what is the refund policy", CancellationToken.None);

        await WaitForAsync(() => conversation.ResponseRequests == 1);
        await Task.Delay(400, TestContext.Current.CancellationToken);

        // One answer, not two: a duplicate would talk over the reply the user is already hearing.
        Assert.Equal(1, conversation.ResponseRequests);
    }

    [Fact]
    public async Task AbandonTurn_WhenTheUserSpeaksAgain_DropsTheTurnBeingPrepared()
    {
        var conversation = new ControllableConversation { RetrievalGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var coordinator = CreateCoordinator(conversation);

        coordinator.BeginTurn("item-1", "what is the refund policy", CancellationToken.None);

        await WaitForAsync(() => conversation.GroundedUtterances.Count == 1);

        coordinator.AbandonTurn();
        conversation.RetrievalGate!.TrySetResult(true);

        await Task.Delay(200, TestContext.Current.CancellationToken);

        // The utterance they talked over is not answered; whatever they are saying now commits its own turn.
        Assert.Equal(0, conversation.ResponseRequests);
    }

    private static GroundedTurnCoordinator CreateCoordinator(
        IRealtimeConversation conversation,
        TimeSpan? acknowledgementDelay = null,
        TimeSpan? watchdog = null)
    {
        return new GroundedTurnCoordinator(
            conversation,
            "session-1",
            acknowledgementDelay,
            watchdog,
            NullLogger.Instance);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + _wait;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("The expected condition was not reached in time.");
    }

    /// <summary>
    /// A conversation whose retrieval can be held open and whose failures can be scripted, so the ordering the
    /// coordinator has to get right is observable rather than a matter of timing.
    /// </summary>
    private sealed class ControllableConversation : IRealtimeConversation
    {
        public bool RespondsAutomatically => false;

        public TaskCompletionSource<bool>? RetrievalGate { get; init; }

        public Exception? RetrievalFailure { get; init; }

        public List<string> GroundedUtterances { get; } = [];

        public List<string> Acknowledgements { get; } = [];

        public int ResponseRequests { get; private set; }

        public async Task<bool> GroundTurnAsync(string utterance, CancellationToken cancellationToken = default)
        {
            lock (GroundedUtterances)
            {
                GroundedUtterances.Add(utterance);
            }

            if (RetrievalFailure is not null)
            {
                throw RetrievalFailure;
            }

            if (RetrievalGate is not null)
            {
                return await RetrievalGate.Task.WaitAsync(cancellationToken);
            }

            return true;
        }

        public Task RequestAcknowledgementAsync(string instructions, CancellationToken cancellationToken = default)
        {
            lock (Acknowledgements)
            {
                Acknowledgements.Add(instructions);
            }

            return Task.CompletedTask;
        }

        public Task RequestResponseAsync(CancellationToken cancellationToken = default)
        {
            ResponseRequests++;

            return Task.CompletedTask;
        }

        public Task SendAudioAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task TruncateAssistantAudioAsync(string itemId, int audioEndMs, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateTurnDetectionAsync(bool allowInterruption, int? silenceDurationMs, float? vadThreshold, string? turnDetectionType = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public IAsyncEnumerable<RealtimeConversationEvent> GetEventsAsync(CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<RealtimeConversationEvent>();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
