#nullable enable
using System.Runtime.CompilerServices;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Chat.Realtime;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Realtime;

/// <summary>
/// Verifies that <see cref="RealtimeChatSessionRunner"/> persists both sides of a realtime conversation to
/// the prompt store, forwards audio and transcript to the sink, attaches tool citations to assistant
/// turns, and persists partial assistant speech on barge-in.
/// </summary>
public sealed class RealtimeChatSessionRunnerTests
{
    [Fact]
    public async Task RunAsync_PersistsUserAndAssistantTurnsAndForwardsToSink()
    {
        var profile = new AIProfile { Type = AIProfileType.Chat, RealtimeDeploymentName = "rt-deploy" };
        var session = new AIChatSession { SessionId = "session-1" };

        var conversation = new FakeConversation(
        [
            Evt(RealtimeConversationEventType.UserTranscript, text: "what is the weather"),
            Evt(RealtimeConversationEventType.AssistantTranscriptDelta, text: "It is "),
            Evt(RealtimeConversationEventType.AssistantTranscriptDelta, text: "sunny."),
            Evt(RealtimeConversationEventType.AssistantAudioDelta, audio: new byte[] { 1, 2, 3 }),
            Evt(RealtimeConversationEventType.AssistantTranscriptDone, text: "It is sunny."),
        ]);
        var orchestrator = new FakeOrchestrator(conversation);
        var (store, persisted) = CreateStore();
        var sink = new RecordingSink();

        using var scope = AIInvocationScope.Begin();
        scope.Context.ToolReferences["[doc:1]"] = new AICompletionReference { Index = 1, Title = "Weather source" };

        var userUtterances = new List<string>();
        var runner = new RealtimeChatSessionRunner(orchestrator, TimeProvider.System, NullLogger<RealtimeChatSessionRunner>.Instance);

        await runner.RunAsync(
            new RealtimeChatRunContext
            {
                Resource = profile,
                SessionId = session.SessionId,
                RealtimeDeploymentName = profile.RealtimeDeploymentName,
                PromptTitle = profile.PromptSubject,
                ChatSession = session,
                Voice = "cedar",
                OnUserUtteranceAsync = (text, _) => { userUtterances.Add(text); return Task.CompletedTask; },
            },
            new ChatSessionRealtimeTurnStore(store.Object),
            PendingAudio(TestContext.Current.CancellationToken),
            sink,
            TestContext.Current.CancellationToken);

        // The orchestrator received the resource, deployment, session, and voice.
        Assert.Equal(profile, orchestrator.LastRequest!.Resource);
        Assert.Equal("rt-deploy", orchestrator.LastRequest!.RealtimeDeploymentName);
        Assert.Equal(session, orchestrator.LastRequest!.ChatSession);
        Assert.Equal("cedar", orchestrator.LastRequest!.Voice);

        // Both turns are persisted, in order.
        Assert.Equal(2, persisted.Count);
        Assert.Equal(ChatRole.User, persisted[0].Role);
        Assert.Equal("what is the weather", persisted[0].Content);
        Assert.Equal("session-1", persisted[0].SessionId);

        Assert.Equal(ChatRole.Assistant, persisted[1].Role);
        Assert.Equal("It is sunny.", persisted[1].Content);
        Assert.NotNull(persisted[1].References);
        Assert.True(persisted[1].References!.ContainsKey("[doc:1]"));

        // Both ends of the transcript, plus audio, reached the client.
        Assert.Equal(["what is the weather"], sink.UserTranscripts);
        Assert.Contains("It is ", sink.AssistantDeltas);
        Assert.Single(sink.AssistantCompleted);
        Assert.Single(sink.AudioChunks);
        Assert.Equal(["what is the weather"], userUtterances);
    }

    [Fact]
    public async Task RunAsync_WithInteractionTurnStore_PersistsInteractionPrompts()
    {
        var interaction = new ChatInteraction { ItemId = "interaction-1" };
        var conversation = new FakeConversation(
        [
            Evt(RealtimeConversationEventType.UserTranscript, text: "hello agent"),
            Evt(RealtimeConversationEventType.AssistantTranscriptDone, text: "Hello!"),
        ]);

        var persisted = new List<ChatInteractionPrompt>();
        var store = new Mock<CrestApps.Core.AI.Chat.IChatInteractionPromptStore>();
        store
            .Setup(s => s.CreateAsync(It.IsAny<ChatInteractionPrompt>(), It.IsAny<CancellationToken>()))
            .Callback<ChatInteractionPrompt, CancellationToken>((prompt, _) => persisted.Add(prompt))
            .Returns(ValueTask.CompletedTask);
        store
            .Setup(s => s.UpdateAsync(It.IsAny<ChatInteractionPrompt>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        store
            .Setup(s => s.DeleteAsync(It.IsAny<ChatInteractionPrompt>(), It.IsAny<CancellationToken>()))
            .Callback<ChatInteractionPrompt, CancellationToken>((prompt, _) => persisted.Remove(prompt))
            .Returns(() => new ValueTask<bool>(true));

        var sink = new RecordingSink();
        using var scope = AIInvocationScope.Begin();
        var runner = new RealtimeChatSessionRunner(new FakeOrchestrator(conversation), TimeProvider.System, NullLogger<RealtimeChatSessionRunner>.Instance);

        await runner.RunAsync(
            new RealtimeChatRunContext { Resource = interaction, SessionId = interaction.ItemId, Interaction = interaction },
            new ChatInteractionRealtimeTurnStore(store.Object),
            PendingAudio(TestContext.Current.CancellationToken),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, persisted.Count);
        Assert.Equal(ChatRole.User, persisted[0].Role);
        Assert.Equal("hello agent", persisted[0].Text);
        Assert.Equal("interaction-1", persisted[0].ChatInteractionId);
        Assert.Equal(ChatRole.Assistant, persisted[1].Role);
        Assert.Equal("Hello!", persisted[1].Text);
    }

    [Fact]
    public async Task RunAsync_OnBargeIn_PersistsPartialAssistantSpeech()
    {
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var session = new AIChatSession { SessionId = "session-2" };

        var conversation = new FakeConversation(
        [
            Evt(RealtimeConversationEventType.AssistantTranscriptDelta, text: "Let me explain in detail"),
            Evt(RealtimeConversationEventType.UserSpeechStarted),
        ]);
        var (store, persisted) = CreateStore();
        var sink = new RecordingSink();

        using var scope = AIInvocationScope.Begin();
        var runner = new RealtimeChatSessionRunner(new FakeOrchestrator(conversation), TimeProvider.System, NullLogger<RealtimeChatSessionRunner>.Instance);

        await runner.RunAsync(
            new RealtimeChatRunContext { Resource = profile, SessionId = session.SessionId, ChatSession = session },
            new ChatSessionRealtimeTurnStore(store.Object),
            PendingAudio(TestContext.Current.CancellationToken),
            sink,
            TestContext.Current.CancellationToken);

        var assistant = Assert.Single(persisted, p => p.Role == ChatRole.Assistant);
        Assert.Equal("Let me explain in detail", assistant.Content);
        Assert.Single(sink.SpeechStarted);
    }

    [Fact]
    public async Task RunAsync_WhenInterruptionDisabled_UserSpeechDoesNotSplitAssistantReply()
    {
        // With barge-in off the provider still raises speech-started when it hears the user, but it does not
        // interrupt the active response. The runner must ignore it so the still-streaming reply is persisted as
        // one turn instead of being flushed early (which split it into two bubbles).
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var session = new AIChatSession { SessionId = "session-3" };

        var conversation = new FakeConversation(
        [
            Evt(RealtimeConversationEventType.AssistantTranscriptDelta, text: "changes in interest "),
            Evt(RealtimeConversationEventType.UserSpeechStarted),
            Evt(RealtimeConversationEventType.AssistantTranscriptDelta, text: "rates and inflation."),
            Evt(RealtimeConversationEventType.AssistantTranscriptDone, text: "changes in interest rates and inflation."),
        ]);
        var (store, persisted) = CreateStore();
        var sink = new RecordingSink();

        using var scope = AIInvocationScope.Begin();
        var runner = new RealtimeChatSessionRunner(new FakeOrchestrator(conversation), TimeProvider.System, NullLogger<RealtimeChatSessionRunner>.Instance);

        await runner.RunAsync(
            new RealtimeChatRunContext { Resource = profile, SessionId = session.SessionId, ChatSession = session, AllowInterruption = false },
            new ChatSessionRealtimeTurnStore(store.Object),
            PendingAudio(TestContext.Current.CancellationToken),
            sink,
            TestContext.Current.CancellationToken);

        // A single, whole assistant turn — the speech-started event did not end it early.
        var assistant = Assert.Single(persisted, p => p.Role == ChatRole.Assistant);
        Assert.Equal("changes in interest rates and inflation.", assistant.Content);
        Assert.Single(sink.AssistantCompleted);
        Assert.Empty(sink.SpeechStarted);
    }

    [Fact]
    public async Task RunAsync_WhenInterruptionDisabled_DropsUtteranceSpokenDuringActiveResponse()
    {
        // Barge-in off: the user speaks "stocks" while the model is still answering the previous prompt. The
        // provider ignores it (active response in progress), so its lagging transcript must not be shown or
        // persisted — only the prompt that was actually answered survives.
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var session = new AIChatSession { SessionId = "session-5" };

        var conversation = new FakeConversation(
        [
            Evt(RealtimeConversationEventType.UserTurnCommitted, itemId: "item-a"),                  // utterance A (answered)
            Evt(RealtimeConversationEventType.ResponseStarted),
            Evt(RealtimeConversationEventType.AssistantTranscriptDelta, text: "Here is the news."),
            Evt(RealtimeConversationEventType.UserTurnCommitted, itemId: "item-b"),                  // utterance B (ignored)
            Evt(RealtimeConversationEventType.UserTranscript, text: "what is happening", itemId: "item-a"),
            Evt(RealtimeConversationEventType.UserTranscript, text: "stocks", itemId: "item-b"),     // -> dropped
            Evt(RealtimeConversationEventType.AssistantTranscriptDone, text: "Here is the news."),
            Evt(RealtimeConversationEventType.ResponseCompleted),
        ]);
        var (store, persisted) = CreateStore();
        var sink = new RecordingSink();

        using var scope = AIInvocationScope.Begin();
        var runner = new RealtimeChatSessionRunner(new FakeOrchestrator(conversation), TimeProvider.System, NullLogger<RealtimeChatSessionRunner>.Instance);

        await runner.RunAsync(
            new RealtimeChatRunContext { Resource = profile, SessionId = session.SessionId, ChatSession = session, AllowInterruption = false },
            new ChatSessionRealtimeTurnStore(store.Object),
            PendingAudio(TestContext.Current.CancellationToken),
            sink,
            TestContext.Current.CancellationToken);

        // Only the answered prompt is surfaced; the ignored "stocks" utterance is dropped from both the live view
        // and the store (it was created at commit time, before the provider's refusal was known).
        Assert.Equal(["what is happening"], sink.UserTranscripts);
        Assert.Single(sink.DroppedUserTurns);
        Assert.Equal(2, sink.PendingUserTurns.Count);
        Assert.Equal(["what is happening"], persisted.Where(p => p.Role == ChatRole.User).Select(p => p.Content));
    }

    [Fact]
    public async Task RunAsync_WhenInterruptionDisabled_KeepsAnUtteranceThatCommittedAfterTheResponseFinished()
    {
        // The provider decides whether to answer an utterance when it COMMITS it, not when speech starts. An
        // utterance that began while the assistant was still talking but committed after the reply finished is
        // answered — and used to have its transcript thrown away, so the prompt vanished from the conversation.
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var session = new AIChatSession { SessionId = "session-11" };

        var conversation = new FakeConversation(
        [
            Evt(RealtimeConversationEventType.ResponseStarted),
            Evt(RealtimeConversationEventType.AssistantTranscriptDone, text: "Here is the news."),
            Evt(RealtimeConversationEventType.ResponseCompleted),
            // The user started talking over the reply, but the commit lands after it completed.
            Evt(RealtimeConversationEventType.UserTurnCommitted, itemId: "item-late"),
            Evt(RealtimeConversationEventType.UserTranscript, text: "and the weather", itemId: "item-late"),
        ]);
        var (store, persisted) = CreateStore();
        var sink = new RecordingSink();

        using var scope = AIInvocationScope.Begin();
        var runner = new RealtimeChatSessionRunner(new FakeOrchestrator(conversation), TimeProvider.System, NullLogger<RealtimeChatSessionRunner>.Instance);

        await runner.RunAsync(
            new RealtimeChatRunContext { Resource = profile, SessionId = session.SessionId, ChatSession = session, AllowInterruption = false },
            new ChatSessionRealtimeTurnStore(store.Object),
            PendingAudio(TestContext.Current.CancellationToken),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(["and the weather"], sink.UserTranscripts);
        Assert.Empty(sink.DroppedUserTurns);
        Assert.Equal(["and the weather"], persisted.Where(p => p.Role == ChatRole.User).Select(p => p.Content));
    }

    [Fact]
    public async Task RunAsync_CreatesTheUserTurnBeforeTheReplyItPrompts()
    {
        // Input-audio transcription lags the spoken reply, so a user turn created when the transcript arrives is
        // stamped after the assistant's answer and history reloads with the prompt underneath its own reply.
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var session = new AIChatSession { SessionId = "session-12" };

        var conversation = new FakeConversation(
        [
            Evt(RealtimeConversationEventType.UserTurnCommitted, itemId: "item-1"),
            Evt(RealtimeConversationEventType.ResponseStarted),
            Evt(RealtimeConversationEventType.AssistantTranscriptDone, text: "It is sunny."),
            Evt(RealtimeConversationEventType.ResponseCompleted),
            Evt(RealtimeConversationEventType.UserTranscript, text: "what is the weather", itemId: "item-1"),
        ]);
        var (store, persisted) = CreateStore();
        var sink = new RecordingSink();

        using var scope = AIInvocationScope.Begin();
        var runner = new RealtimeChatSessionRunner(new FakeOrchestrator(conversation), TimeProvider.System, NullLogger<RealtimeChatSessionRunner>.Instance);

        await runner.RunAsync(
            new RealtimeChatRunContext { Resource = profile, SessionId = session.SessionId, ChatSession = session },
            new ChatSessionRealtimeTurnStore(store.Object),
            PendingAudio(TestContext.Current.CancellationToken),
            sink,
            TestContext.Current.CancellationToken);

        var user = Assert.Single(persisted, p => p.Role == ChatRole.User);
        var assistant = Assert.Single(persisted, p => p.Role == ChatRole.Assistant);

        // The transcript arrived last, but the turn it filled in was created first and keeps that timestamp.
        Assert.Equal("what is the weather", user.Content);
        Assert.True(user.CreatedUtc <= assistant.CreatedUtc);
        Assert.Single(sink.PendingUserTurns);
    }

    [Fact]
    public async Task RunAsync_OnBargeIn_TellsTheProviderHowMuchOfTheReplyWasHeard()
    {
        // Without truncation the model keeps the whole generated reply in its context and believes it said things
        // the user never heard, so "what did you just say?" answers with text that was cut off.
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var session = new AIChatSession { SessionId = "session-13" };

        // 1 s of PCM16 @ 24 kHz handed to the transport for one assistant item.
        var oneSecond = new byte[24000 * 2];

        var conversation = new FakeConversation(
        [
            Evt(RealtimeConversationEventType.ResponseStarted),
            Evt(RealtimeConversationEventType.AssistantAudioDelta, audio: oneSecond, itemId: "assistant-item"),
            Evt(RealtimeConversationEventType.AssistantTranscriptDelta, text: "A very long answer"),
            Evt(RealtimeConversationEventType.UserSpeechStarted),
        ]);
        var (store, _) = CreateStore();

        // The transport is still holding 400 ms of it, so the user heard about 600 ms (less the jitter buffer).
        var sink = new RecordingSink { PendingPlaybackMs = 400 };

        using var scope = AIInvocationScope.Begin();
        var runner = new RealtimeChatSessionRunner(new FakeOrchestrator(conversation), TimeProvider.System, NullLogger<RealtimeChatSessionRunner>.Instance);

        await runner.RunAsync(
            new RealtimeChatRunContext { Resource = profile, SessionId = session.SessionId, ChatSession = session, AllowInterruption = true },
            new ChatSessionRealtimeTurnStore(store.Object),
            PendingAudio(TestContext.Current.CancellationToken),
            sink,
            TestContext.Current.CancellationToken);

        var truncation = Assert.Single(conversation.Truncations);
        Assert.Equal("assistant-item", truncation.ItemId);
        Assert.Equal(1000 - 400 - 80, truncation.AudioEndMs);
    }

    [Fact]
    public async Task RunAsync_WhenInterruptionDisabled_FlushesStalePlaybackWhenNewResponseStarts()
    {
        // Barge-in off: when a follow-up's response starts while the previous reply's paced audio is still draining,
        // the runner flushes playback so the newest reply plays instead of the stale one finishing first. Barge-in
        // on does not flush here (it flushes on the user's barge-in instead).
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var session = new AIChatSession { SessionId = "session-6" };

        RealtimeConversationEvent[] Events() =>
        [
            Evt(RealtimeConversationEventType.ResponseStarted),
            Evt(RealtimeConversationEventType.AssistantTranscriptDone, text: "first"),
            Evt(RealtimeConversationEventType.ResponseCompleted),
            Evt(RealtimeConversationEventType.ResponseStarted),
            Evt(RealtimeConversationEventType.AssistantTranscriptDone, text: "second"),
            Evt(RealtimeConversationEventType.ResponseCompleted),
        ];

        var offSink = new RecordingSink();
        using (var scope = AIInvocationScope.Begin())
        {
            var (store, _) = CreateStore();
            var runner = new RealtimeChatSessionRunner(new FakeOrchestrator(new FakeConversation(Events())), TimeProvider.System, NullLogger<RealtimeChatSessionRunner>.Instance);
            await runner.RunAsync(
                new RealtimeChatRunContext { Resource = profile, SessionId = session.SessionId, ChatSession = session, AllowInterruption = false },
                new ChatSessionRealtimeTurnStore(store.Object),
                PendingAudio(TestContext.Current.CancellationToken),
                offSink,
                TestContext.Current.CancellationToken);
        }

        // One flush per response start while barge-in is off.
        Assert.Equal(2, offSink.FlushedPlayback.Count);

        var onSink = new RecordingSink();
        using (var scope = AIInvocationScope.Begin())
        {
            var (store, _) = CreateStore();
            var runner = new RealtimeChatSessionRunner(new FakeOrchestrator(new FakeConversation(Events())), TimeProvider.System, NullLogger<RealtimeChatSessionRunner>.Instance);
            await runner.RunAsync(
                new RealtimeChatRunContext { Resource = profile, SessionId = session.SessionId, ChatSession = session, AllowInterruption = true },
                new ChatSessionRealtimeTurnStore(store.Object),
                PendingAudio(TestContext.Current.CancellationToken),
                onSink,
                TestContext.Current.CancellationToken);
        }

        // Barge-in on never flushes on response start.
        Assert.Empty(onSink.FlushedPlayback);
    }

    [Fact]
    public async Task RunAsync_SwallowsBenignActiveResponseError()
    {
        // "Conversation already has an active response in progress" is an expected race when the user speaks
        // over the model with barge-in off — it must be logged, not shown to the user as a chat message.
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var session = new AIChatSession { SessionId = "session-4" };

        var conversation = new FakeConversation(
        [
            new RealtimeConversationEvent { Type = RealtimeConversationEventType.Error, ErrorMessage = "Conversation already has an active response in progress: resp_abc" },
            new RealtimeConversationEvent { Type = RealtimeConversationEventType.Error, ErrorMessage = "Something actually went wrong." },
        ]);
        var (store, _) = CreateStore();
        var sink = new RecordingSink();

        using var scope = AIInvocationScope.Begin();
        var runner = new RealtimeChatSessionRunner(new FakeOrchestrator(conversation), TimeProvider.System, NullLogger<RealtimeChatSessionRunner>.Instance);

        await runner.RunAsync(
            new RealtimeChatRunContext { Resource = profile, SessionId = session.SessionId, ChatSession = session },
            new ChatSessionRealtimeTurnStore(store.Object),
            PendingAudio(TestContext.Current.CancellationToken),
            sink,
            TestContext.Current.CancellationToken);

        // The benign error was suppressed; the genuine error still reached the user.
        Assert.Equal(["Something actually went wrong."], sink.Errors);
    }

    [Fact]
    public async Task RunAsync_ReportsSessionReadyAndEndedToTheClient()
    {
        // Without an end signal the browser keeps the microphone open and streams audio into a session that no
        // longer exists, with the button still showing "End Conversation".
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var session = new AIChatSession { SessionId = "session-7" };

        var conversation = new FakeConversation([Evt(RealtimeConversationEventType.AssistantTranscriptDone, text: "Hello.")]);
        var (store, _) = CreateStore();
        var sink = new RecordingSink();

        using var scope = AIInvocationScope.Begin();
        var runner = new RealtimeChatSessionRunner(new FakeOrchestrator(conversation), TimeProvider.System, NullLogger<RealtimeChatSessionRunner>.Instance);

        await runner.RunAsync(
            new RealtimeChatRunContext { Resource = profile, SessionId = session.SessionId, ChatSession = session },
            new ChatSessionRealtimeTurnStore(store.Object),
            PendingAudio(TestContext.Current.CancellationToken),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(["session-7"], sink.SessionReady);
        Assert.Equal([RealtimeSessionEndReasons.Completed], sink.SessionEnded);
    }

    [Fact]
    public async Task RunAsync_WhenTranscriptionFails_KeepsIgnoredUtteranceBookkeepingAligned()
    {
        // Barge-in off pairs each utterance with the transcript that follows it. When transcription fails for one
        // utterance no transcript arrives for it, so without consuming its slot every later transcript is matched
        // against the wrong utterance — and an answered prompt silently disappears from the conversation.
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var session = new AIChatSession { SessionId = "session-8" };

        var conversation = new FakeConversation(
        [
            Evt(RealtimeConversationEventType.UserTurnCommitted, itemId: "item-a"),      // utterance A (answered)
            Evt(RealtimeConversationEventType.UserTranscriptFailed, itemId: "item-a"),   // A's transcription fails
            Evt(RealtimeConversationEventType.UserTurnCommitted, itemId: "item-b"),      // utterance B (answered)
            Evt(RealtimeConversationEventType.UserTranscript, text: "what is the news", itemId: "item-b"),
        ]);
        var (store, persisted) = CreateStore();
        var sink = new RecordingSink();

        using var scope = AIInvocationScope.Begin();
        var runner = new RealtimeChatSessionRunner(new FakeOrchestrator(conversation), TimeProvider.System, NullLogger<RealtimeChatSessionRunner>.Instance);

        await runner.RunAsync(
            new RealtimeChatRunContext { Resource = profile, SessionId = session.SessionId, ChatSession = session, AllowInterruption = false },
            new ChatSessionRealtimeTurnStore(store.Object),
            PendingAudio(TestContext.Current.CancellationToken),
            sink,
            TestContext.Current.CancellationToken);

        // The failed utterance is removed rather than shifting the pairing, so B's transcript still lands on B.
        Assert.Equal(["what is the news"], sink.UserTranscripts);
        Assert.Single(sink.DroppedUserTurns);
        Assert.Equal(["what is the news"], persisted.Where(p => p.Role == ChatRole.User).Select(p => p.Content));
    }

    [Fact]
    public async Task RunAsync_WhenResponseFails_FlushesTheTurnAndSurfacesTheReason()
    {
        // A response that ends "failed" (rate limit, content filter, token cap) still produced text. Leaving it
        // un-flushed made the next reply's deltas append to the same bubble, and the user was never told why the
        // answer stopped.
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var session = new AIChatSession { SessionId = "session-9" };

        var conversation = new FakeConversation(
        [
            Evt(RealtimeConversationEventType.ResponseStarted),
            Evt(RealtimeConversationEventType.AssistantTranscriptDelta, text: "Partly through the ans"),
            new RealtimeConversationEvent
            {
                Type = RealtimeConversationEventType.ResponseCompleted,
                ResponseStatus = "failed",
                ErrorMessage = "Rate limit reached.",
            },
            Evt(RealtimeConversationEventType.ResponseStarted),
            Evt(RealtimeConversationEventType.AssistantTranscriptDone, text: "A fresh answer."),
        ]);
        var (store, persisted) = CreateStore();
        var sink = new RecordingSink();

        using var scope = AIInvocationScope.Begin();
        var runner = new RealtimeChatSessionRunner(new FakeOrchestrator(conversation), TimeProvider.System, NullLogger<RealtimeChatSessionRunner>.Instance);

        await runner.RunAsync(
            new RealtimeChatRunContext { Resource = profile, SessionId = session.SessionId, ChatSession = session },
            new ChatSessionRealtimeTurnStore(store.Object),
            PendingAudio(TestContext.Current.CancellationToken),
            sink,
            TestContext.Current.CancellationToken);

        // Two separate assistant turns, not one merged bubble.
        Assert.Equal(
            ["Partly through the ans", "A fresh answer."],
            persisted.Where(p => p.Role == ChatRole.Assistant).Select(p => p.Content));
        Assert.Equal(["Rate limit reached."], sink.Errors);
    }

    [Fact]
    public async Task RunAsync_WhenInterruptionDisabled_HoldsTheMicClosedUntilQueuedAudioHasDrained()
    {
        // The provider saying "done" is not the user having heard the reply: on a paced transport seconds of
        // audio can still be queued. Reopening the half-duplex mic gate at response.done let the tail of the
        // assistant's own voice back into the provider.
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var session = new AIChatSession { SessionId = "session-10" };

        // Deliberately ordered rather than timed: the microphone only starts producing once the runner has
        // finished processing the completed response, so the assertion is about the gate, not about timing.
        var conversation = new FakeConversation(
        [
            Evt(RealtimeConversationEventType.ResponseStarted),
            Evt(RealtimeConversationEventType.ResponseCompleted),
        ])
        {
            HoldOpen = true,
        };
        var (store, _) = CreateStore();
        var sink = new RecordingSink { PendingPlaybackMs = 60_000 };

        using var scope = AIInvocationScope.Begin();
        var runner = new RealtimeChatSessionRunner(new FakeOrchestrator(conversation), TimeProvider.System, NullLogger<RealtimeChatSessionRunner>.Instance);

        async IAsyncEnumerable<ReadOnlyMemory<byte>> MicrophoneAsync()
        {
            await conversation.EventsDrained.Task;

            for (var i = 0; i < 5; i++)
            {
                yield return new byte[100 * 24000 / 1000 * 2];
            }

            conversation.Release();
        }

        await runner.RunAsync(
            new RealtimeChatRunContext { Resource = profile, SessionId = session.SessionId, ChatSession = session, AllowInterruption = false },
            new ChatSessionRealtimeTurnStore(store.Object),
            MicrophoneAsync(),
            sink,
            TestContext.Current.CancellationToken);

        // The queued minute of playback had not drained, so nothing the microphone picked up was forwarded.
        Assert.Empty(conversation.SentAudio);
    }

    [Fact]
    public async Task RunAsync_WhenSettingsChangeMidSession_AppliesToBothTheProviderAndTheInputPump()
    {
        // Barge-in is enforced by the browser gate, the server input pump and the provider's turn detection at
        // once. Changing only one of them leaves them disagreeing until the next session — the symptom being an
        // assistant that still interrupts itself after the user switched interruptions off.
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var session = new AIChatSession { SessionId = "session-14" };

        var conversation = new FakeConversation([]) { HoldOpen = true };
        var (store, _) = CreateStore();
        var sink = new RecordingSink();
        RealtimeSessionControl? control = null;

        using var scope = AIInvocationScope.Begin();
        var runner = new RealtimeChatSessionRunner(new FakeOrchestrator(conversation), TimeProvider.System, NullLogger<RealtimeChatSessionRunner>.Instance);

        var context = new RealtimeChatRunContext
        {
            Resource = profile,
            SessionId = session.SessionId,
            ChatSession = session,
            AllowInterruption = true,
            OnSessionStarted = c => control = c,
        };

        async IAsyncEnumerable<ReadOnlyMemory<byte>> MicrophoneAsync()
        {
            await conversation.EventsDrained.Task;
            Assert.NotNull(control);

            await control!.ApplyTurnDetectionAsync(allowInterruption: false, silenceDurationMs: 900, vadThreshold: 0.7f);

            conversation.Release();
            yield break;
        }

        await runner.RunAsync(context, new ChatSessionRealtimeTurnStore(store.Object), MicrophoneAsync(), sink, TestContext.Current.CancellationToken);

        // The provider was told, and the pump's own view changed with it.
        var update = Assert.Single(conversation.TurnDetectionUpdates);
        Assert.False(update.AllowInterruption);
        Assert.Equal(900, update.SilenceMs);
        Assert.Equal(0.7f, update.Threshold);
        Assert.False(context.AllowInterruption);
    }

    [Fact]
    public async Task RunAsync_WhenNobodySpeaks_EndsTheSessionAsIdle()
    {
        // A realtime session holds an open, billed provider connection whether or not anyone is talking, so a
        // forgotten tab must not keep one alive until the provider's own hour-long cap closes it.
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var session = new AIChatSession { SessionId = "session-15" };

        // The conversation is held open and the microphone never produces anything, so nothing races the
        // watchdog: a short window keeps the test fast without making the outcome timing-dependent.
        var conversation = new FakeConversation([]) { HoldOpen = true };
        var (store, _) = CreateStore();
        var sink = new RecordingSink();

        using var scope = AIInvocationScope.Begin();
        var runner = new RealtimeChatSessionRunner(new FakeOrchestrator(conversation), TimeProvider.System, NullLogger<RealtimeChatSessionRunner>.Instance);

        await runner.RunAsync(
            new RealtimeChatRunContext
            {
                Resource = profile,
                SessionId = session.SessionId,
                ChatSession = session,
                IdleTimeout = TimeSpan.FromMilliseconds(250),
            },
            new ChatSessionRealtimeTurnStore(store.Object),
            PendingAudio(TestContext.Current.CancellationToken),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal([RealtimeSessionEndReasons.Idle], sink.SessionEnded);
    }

    private static RealtimeConversationEvent Evt(
        RealtimeConversationEventType type,
        string? text = null,
        byte[]? audio = null,
        string? itemId = null)
        => new()
        {
            Type = type,
            Text = text!,
            Audio = audio ?? ReadOnlyMemory<byte>.Empty,
            ItemId = itemId!,
        };

    private static (Mock<IAIChatSessionPromptStore> Store, List<AIChatSessionPrompt> Persisted) CreateStore()
    {
        var persisted = new List<AIChatSessionPrompt>();
        var store = new Mock<IAIChatSessionPromptStore>();
        store
            .Setup(s => s.CreateAsync(It.IsAny<AIChatSessionPrompt>(), It.IsAny<CancellationToken>()))
            .Callback<AIChatSessionPrompt, CancellationToken>((prompt, _) => persisted.Add(prompt))
            .Returns(ValueTask.CompletedTask);
        // A realtime user turn is written when the utterance is committed and filled in (or removed) once its
        // transcription resolves, so the fake has to honour updates and deletes for the list to reflect history.
        store
            .Setup(s => s.UpdateAsync(It.IsAny<AIChatSessionPrompt>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        store
            .Setup(s => s.DeleteAsync(It.IsAny<AIChatSessionPrompt>(), It.IsAny<CancellationToken>()))
            .Callback<AIChatSessionPrompt, CancellationToken>((prompt, _) => persisted.Remove(prompt))
            .Returns(() => new ValueTask<bool>(true));

        return (store, persisted);
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> PendingAudio([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Model an open microphone: stay pending until the runner cancels the input pump (which it does
        // once the outbound event stream ends).
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield break;
    }

    private sealed class FakeOrchestrator : IRealtimeOrchestrator
    {
        private readonly IRealtimeConversation _conversation;

        public FakeOrchestrator(IRealtimeConversation conversation)
        {
            _conversation = conversation;
        }

        public RealtimeOrchestrationRequest? LastRequest { get; private set; }

        public Task<IRealtimeConversation> StartAsync(RealtimeOrchestrationRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            return Task.FromResult(_conversation);
        }
    }

    private sealed class FakeConversation : IRealtimeConversation
    {
        private readonly IReadOnlyList<RealtimeConversationEvent> _events;

        public FakeConversation(IReadOnlyList<RealtimeConversationEvent> events)
        {
            _events = events;
        }

        public List<ReadOnlyMemory<byte>> SentAudio { get; } = [];

        /// <summary>
        /// Completes once every scripted event has been consumed by the runner, so a test can order what it does
        /// next after the runner has finished reacting to them.
        /// </summary>
        public TaskCompletionSource EventsDrained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Keeps the event stream open after the scripted events until <see cref="Release"/> is called, so the
        /// session does not tear down while a test is still exercising the input pump.
        /// </summary>
        public bool HoldOpen { get; init; }

        private readonly TaskCompletionSource _hold = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _hold.TrySetResult();

        public List<(string ItemId, int AudioEndMs)> Truncations { get; } = [];

        public List<(bool AllowInterruption, int? SilenceMs, float? Threshold)> TurnDetectionUpdates { get; } = [];

        public Task SendAudioAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken = default)
        {
            SentAudio.Add(audio);

            return Task.CompletedTask;
        }

        public Task TruncateAssistantAudioAsync(string itemId, int audioEndMs, CancellationToken cancellationToken = default)
        {
            Truncations.Add((itemId, audioEndMs));

            return Task.CompletedTask;
        }

        public Task UpdateTurnDetectionAsync(bool allowInterruption, int? silenceDurationMs, float? vadThreshold, CancellationToken cancellationToken = default)
        {
            TurnDetectionUpdates.Add((allowInterruption, silenceDurationMs, vadThreshold));

            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<RealtimeConversationEvent> GetEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var evt in _events)
            {
                cancellationToken.ThrowIfCancellationRequested();

                yield return evt;

                await Task.Yield();
            }

            EventsDrained.TrySetResult();

            if (HoldOpen)
            {
                await _hold.Task.WaitAsync(cancellationToken);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingSink : IRealtimeConversationSink
    {
        public List<string> UserTranscripts { get; } = [];

        public List<string> AssistantDeltas { get; } = [];

        public List<string> AssistantCompleted { get; } = [];

        public List<ReadOnlyMemory<byte>> AudioChunks { get; } = [];

        public List<string> SpeechStarted { get; } = [];

        public List<string> SessionReady { get; } = [];

        public List<string> SessionEnded { get; } = [];

        public List<string> PendingUserTurns { get; } = [];

        public List<string> DroppedUserTurns { get; } = [];

        public int PendingPlaybackMs { get; set; }

        public List<string> FlushedPlayback { get; } = [];

        public List<string> Errors { get; } = [];

        public Task AssistantAudioAsync(string identifier, ReadOnlyMemory<byte> audio, CancellationToken cancellationToken)
        {
            AudioChunks.Add(audio);

            return Task.CompletedTask;
        }

        public Task UserTranscriptAsync(string identifier, string turnId, string text, CancellationToken cancellationToken)
        {
            UserTranscripts.Add(text);

            return Task.CompletedTask;
        }

        public Task UserTurnPendingAsync(string identifier, string turnId, CancellationToken cancellationToken)
        {
            PendingUserTurns.Add(turnId);

            return Task.CompletedTask;
        }

        public Task UserTurnDroppedAsync(string identifier, string turnId, CancellationToken cancellationToken)
        {
            DroppedUserTurns.Add(turnId);

            return Task.CompletedTask;
        }

        public Task AssistantTranscriptDeltaAsync(string identifier, string messageId, string text, string responseId, Dictionary<string, AICompletionReference>? references, CancellationToken cancellationToken)
        {
            AssistantDeltas.Add(text);

            return Task.CompletedTask;
        }

        public Task AssistantCompletedAsync(string identifier, string messageId, Dictionary<string, AICompletionReference>? references, CancellationToken cancellationToken)
        {
            AssistantCompleted.Add(messageId);

            return Task.CompletedTask;
        }

        public Task SessionReadyAsync(string identifier, CancellationToken cancellationToken)
        {
            SessionReady.Add(identifier);

            return Task.CompletedTask;
        }

        public Task SessionEndedAsync(string identifier, string reason, CancellationToken cancellationToken)
        {
            SessionEnded.Add(reason);

            return Task.CompletedTask;
        }

        public Task SpeechStartedAsync(string identifier, CancellationToken cancellationToken)
        {
            SpeechStarted.Add(identifier);

            return Task.CompletedTask;
        }

        public Task FlushPlaybackAsync(string identifier, CancellationToken cancellationToken)
        {
            FlushedPlayback.Add(identifier);

            return Task.CompletedTask;
        }

        public Task ErrorAsync(string message, CancellationToken cancellationToken)
        {
            Errors.Add(message);

            return Task.CompletedTask;
        }
    }
}
