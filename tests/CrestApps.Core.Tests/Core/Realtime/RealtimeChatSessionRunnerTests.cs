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
            Evt(RealtimeConversationEventType.UserSpeechStarted),                                   // utterance A (answered)
            Evt(RealtimeConversationEventType.ResponseStarted),
            Evt(RealtimeConversationEventType.AssistantTranscriptDelta, text: "Here is the news."),
            Evt(RealtimeConversationEventType.UserSpeechStarted),                                   // utterance B (ignored)
            Evt(RealtimeConversationEventType.UserTranscript, text: "what is happening"),           // A's lagging transcript
            Evt(RealtimeConversationEventType.UserTranscript, text: "stocks"),                      // B's transcript -> dropped
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

        // Only the answered prompt is surfaced and persisted; the ignored "stocks" utterance is dropped.
        Assert.Equal(["what is happening"], sink.UserTranscripts);
        Assert.Equal(["what is happening"], persisted.Where(p => p.Role == ChatRole.User).Select(p => p.Content));
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

    private static RealtimeConversationEvent Evt(RealtimeConversationEventType type, string? text = null, byte[]? audio = null)
        => new()
        {
            Type = type,
            Text = text!,
            Audio = audio ?? ReadOnlyMemory<byte>.Empty,
        };

    private static (Mock<IAIChatSessionPromptStore> Store, List<AIChatSessionPrompt> Persisted) CreateStore()
    {
        var persisted = new List<AIChatSessionPrompt>();
        var store = new Mock<IAIChatSessionPromptStore>();
        store
            .Setup(s => s.CreateAsync(It.IsAny<AIChatSessionPrompt>(), It.IsAny<CancellationToken>()))
            .Callback<AIChatSessionPrompt, CancellationToken>((prompt, _) => persisted.Add(prompt))
            .Returns(ValueTask.CompletedTask);

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

        public Task SendAudioAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken = default)
        {
            SentAudio.Add(audio);

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

        public List<string> Errors { get; } = [];

        public Task AssistantAudioAsync(string identifier, ReadOnlyMemory<byte> audio, CancellationToken cancellationToken)
        {
            AudioChunks.Add(audio);

            return Task.CompletedTask;
        }

        public Task UserTranscriptAsync(string identifier, string text, CancellationToken cancellationToken)
        {
            UserTranscripts.Add(text);

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

        public Task SpeechStartedAsync(string identifier, CancellationToken cancellationToken)
        {
            SpeechStarted.Add(identifier);

            return Task.CompletedTask;
        }

        public Task ErrorAsync(string message, CancellationToken cancellationToken)
        {
            Errors.Add(message);

            return Task.CompletedTask;
        }
    }
}
