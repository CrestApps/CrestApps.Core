#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only.
#nullable enable
using System.Text.Json;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Realtime;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Realtime;

/// <summary>
/// Covers the per-turn knowledge retrieval that replaces the preemptive RAG a realtime session cannot run at
/// PREPARE time: how a session is configured for it, what it sends the provider, and the fallbacks that keep a
/// grounded session from going silent.
/// </summary>
public sealed class RealtimeTurnGroundingTests
{
    [Fact]
    public void Configure_WhenResponsesAreDeferred_TurnsOffProviderResponseCreation()
    {
        var options = new DefaultRealtimeSessionConfigurator().Configure(new RealtimeSessionConfiguratorContext
        {
            Model = "gpt-realtime",
            CreateResponseAutomatically = false,
        });

        var turnDetection = Assert.IsType<RealtimeTurnDetectionOverrides>(options.RawRepresentationFactory!());
        Assert.False(turnDetection.CreateResponse);
    }

    [Fact]
    public void Configure_WithServerVadAndDeferredResponses_TurnsOffProviderResponseCreation()
    {
        var options = new DefaultRealtimeSessionConfigurator().Configure(new RealtimeSessionConfiguratorContext
        {
            Model = "gpt-realtime",
            TurnDetectionType = RealtimeTurnDetectionTypes.ServerVad,
            CreateResponseAutomatically = false,
        });

        var turnDetection = Assert.IsType<RealtimeTurnDetectionOverrides>(options.RawRepresentationFactory!());
        Assert.Equal(RealtimeTurnDetectionTypes.ServerVad, turnDetection.Type);
        Assert.False(turnDetection.CreateResponse);
    }

    [Fact]
    public void Configure_ByDefault_LeavesProviderResponseCreationOn()
    {
        var options = new DefaultRealtimeSessionConfigurator().Configure(new RealtimeSessionConfiguratorContext
        {
            Model = "gpt-realtime",
        });

        var turnDetection = Assert.IsType<RealtimeTurnDetectionOverrides>(options.RawRepresentationFactory!());
        Assert.True(turnDetection.CreateResponse);
    }

    [Fact]
    public async Task GroundTurnAsync_WhenRetrievalReturnsContent_AddsItAsASystemItem()
    {
        var session = new RecordingSession();
        var conversation = new DefaultRealtimeConversation(session, (utterance, _) => Task.FromResult<string?>($"CONTEXT FOR: {utterance}"));

        Assert.False(conversation.RespondsAutomatically);

        var grounded = await conversation.GroundTurnAsync("what is the refund policy", TestContext.Current.CancellationToken);

        Assert.True(grounded);

        var item = Assert.IsType<CreateConversationItemRealtimeClientMessage>(Assert.Single(session.Sent));
        Assert.Equal(ChatRole.System, item.Item!.Role);
        Assert.Equal("CONTEXT FOR: what is the refund policy", Assert.IsType<TextContent>(Assert.Single(item.Item.Contents!)).Text);
    }

    [Fact]
    public async Task GroundTurnAsync_WhenNothingRetrieved_SendsNothing()
    {
        var session = new RecordingSession();
        var conversation = new DefaultRealtimeConversation(session, (_, _) => Task.FromResult<string?>(null));

        var grounded = await conversation.GroundTurnAsync("hello", TestContext.Current.CancellationToken);

        Assert.False(grounded);
        Assert.Empty(session.Sent);
    }

    [Fact]
    public async Task RequestResponseAsync_OnDeferredSession_AsksTheModelToAnswer()
    {
        var session = new RecordingSession();
        var conversation = new DefaultRealtimeConversation(session, (_, _) => Task.FromResult<string?>(null));

        await conversation.RequestResponseAsync(TestContext.Current.CancellationToken);

        Assert.IsType<CreateResponseRealtimeClientMessage>(Assert.Single(session.Sent));
    }

    [Fact]
    public async Task RequestResponseAsync_OnAutomaticSession_IsANoOp()
    {
        // The provider already answers for this session; a second response.create would produce a duplicate reply.
        var session = new RecordingSession();
        var conversation = new DefaultRealtimeConversation(session);

        Assert.True(conversation.RespondsAutomatically);

        await conversation.RequestResponseAsync(TestContext.Current.CancellationToken);
        await conversation.GroundTurnAsync("hello", TestContext.Current.CancellationToken);

        Assert.Empty(session.Sent);
    }

    [Fact]
    public async Task UpdateTurnDetectionAsync_OnAGroundedSession_KeepsResponseCreationDeferred()
    {
        // Toggling barge-in mid-call must not hand response creation back to the provider: it would answer
        // before the knowledge for the turn has been retrieved, and then answer again when the host asks.
        var session = new RecordingSession
        {
            Options = new RealtimeSessionOptions
            {
                RawRepresentationFactory = () => new RealtimeTurnDetectionOverrides
                {
                    Type = RealtimeTurnDetectionTypes.SemanticVad,
                    CreateResponse = false,
                },
            },
        };

        var conversation = new DefaultRealtimeConversation(session, (_, _) => Task.FromResult<string?>(null));

        await conversation.UpdateTurnDetectionAsync(allowInterruption: false, silenceDurationMs: null, vadThreshold: null, cancellationToken: TestContext.Current.CancellationToken);

        var raw = Assert.Single(session.Sent).RawRepresentation as string;
        Assert.NotNull(raw);

        using var document = JsonDocument.Parse(raw!);
        var turnDetection = document.RootElement
            .GetProperty("session")
            .GetProperty("audio")
            .GetProperty("input")
            .GetProperty("turn_detection");

        Assert.False(turnDetection.GetProperty("create_response").GetBoolean());
    }

    [Fact]
    public async Task RetrieveAsync_RunsThePreemptiveHandlersAndReturnsWhatTheyWrote()
    {
        var handler = new RecordingPreemptiveRagHandler("KNOWLEDGE BLOCK");
        var grounding = CreateGrounding(handler, preemptiveEnabled: true);
        var context = CreateContext(dataSourceId: "ds-1");

        using var scope = AIInvocationScope.Begin();

        var retrieved = await grounding.RetrieveAsync(context, new AIProfile(), "how do I reset my password", TestContext.Current.CancellationToken);

        Assert.Equal("KNOWLEDGE BLOCK", retrieved);

        // The handlers must see the utterance as the user message — that is the only query they have.
        Assert.Equal("how do I reset my password", handler.ObservedUserMessage);

        // ...and the session's completion context, so the data source and tool state are identical to the
        // session's own.
        Assert.Same(context.CompletionContext, handler.ObservedCompletionContext);
    }

    [Fact]
    public async Task RetrieveAsync_PromotesCitationsOntoTheInvocationScope()
    {
        // A realtime turn reads its citations from the ambient scope, where tool-driven citations already land.
        // Without this, a grounded answer would quote sources it never showed the user.
        var reference = new AICompletionReference { Index = 1, Title = "Refund policy", ReferenceId = "kb-1" };
        var handler = new RecordingPreemptiveRagHandler("KNOWLEDGE BLOCK")
        {
            References = new Dictionary<string, AICompletionReference> { ["[doc:1]"] = reference },
        };

        var grounding = CreateGrounding(handler, preemptiveEnabled: true);

        using var scope = AIInvocationScope.Begin();

        await grounding.RetrieveAsync(CreateContext(dataSourceId: "ds-1"), new AIProfile(), "refunds?", TestContext.Current.CancellationToken);

        Assert.Same(reference, AIInvocationScope.Current!.ToolReferences["[doc:1]"]);
    }

    [Fact]
    public async Task RetrieveAsync_WhenAHandlerThrows_StillReturnsWhatTheOthersFound()
    {
        // Losing one source must cost context, not the whole answer.
        var failing = new RecordingPreemptiveRagHandler("never reached") { Throws = true };
        var working = new RecordingPreemptiveRagHandler("KNOWLEDGE BLOCK");
        var grounding = CreateGrounding([failing, working], preemptiveEnabled: true);

        using var scope = AIInvocationScope.Begin();

        var retrieved = await grounding.RetrieveAsync(CreateContext(dataSourceId: "ds-1"), new AIProfile(), "refunds?", TestContext.Current.CancellationToken);

        Assert.Equal("KNOWLEDGE BLOCK", retrieved);
    }

    [Fact]
    public void IsGroundingAvailable_WithADataSource_IsOn()
    {
        var grounding = CreateGrounding(new RecordingPreemptiveRagHandler("x"), preemptiveEnabled: true);

        Assert.True(grounding.IsGroundingAvailable(CreateContext(dataSourceId: "ds-1"), new AIProfile()));
    }

    [Fact]
    public void IsGroundingAvailable_WithDocumentsButNoDataSource_IsOn()
    {
        var grounding = CreateGrounding(new RecordingPreemptiveRagHandler("x"), preemptiveEnabled: true);
        var context = CreateContext(dataSourceId: null);
        context.CompletionContext!.AdditionalProperties[AICompletionContextKeys.HasDocuments] = true;

        Assert.True(grounding.IsGroundingAvailable(context, new AIProfile()));
    }

    [Fact]
    public void IsGroundingAvailable_WithNothingToRetrieveFrom_IsOff()
    {
        var grounding = CreateGrounding(new RecordingPreemptiveRagHandler("x"), preemptiveEnabled: true);

        Assert.False(grounding.IsGroundingAvailable(CreateContext(dataSourceId: null), new AIProfile()));
    }

    [Fact]
    public void IsGroundingAvailable_WhenPreemptiveRagIsSwitchedOffSiteWide_IsOff()
    {
        // The site-wide switch governs voice exactly as it governs text: the model decides when to search.
        var grounding = CreateGrounding(new RecordingPreemptiveRagHandler("x"), preemptiveEnabled: false);

        Assert.False(grounding.IsGroundingAvailable(CreateContext(dataSourceId: "ds-1"), new AIProfile()));
    }

    [Fact]
    public void IsGroundingAvailable_WithNoHandlersRegistered_IsOff()
    {
        var grounding = CreateGrounding([], preemptiveEnabled: true);

        Assert.False(grounding.IsGroundingAvailable(CreateContext(dataSourceId: "ds-1"), new AIProfile()));
    }

    private static OrchestrationContext CreateContext(string? dataSourceId)
    {
        return new OrchestrationContext
        {
            CompletionContext = new AICompletionContext
            {
                DataSourceId = dataSourceId,
                SystemMessage = "You are a helpful realtime assistant.",
            },
        };
    }

    private static DefaultRealtimeTurnGrounding CreateGrounding(IPreemptiveRagHandler handler, bool preemptiveEnabled)
        => CreateGrounding([handler], preemptiveEnabled);

    private static DefaultRealtimeTurnGrounding CreateGrounding(IPreemptiveRagHandler[] handlers, bool preemptiveEnabled)
    {
        return new DefaultRealtimeTurnGrounding(
            handlers,
            new TestOptionsMonitor<DefaultOrchestratorSettings> { CurrentValue = new DefaultOrchestratorSettings { EnablePreemptiveRag = preemptiveEnabled } },
            NullLogger<DefaultRealtimeTurnGrounding>.Instance);
    }

    /// <summary>
    /// A preemptive RAG handler that records what it was asked and writes a fixed block, standing in for the
    /// data source and document handlers the real pipeline runs.
    /// </summary>
    private sealed class RecordingPreemptiveRagHandler : IPreemptiveRagHandler
    {
        private readonly string _block;

        public RecordingPreemptiveRagHandler(string block)
        {
            _block = block;
        }

        public bool Throws { get; init; }

        public Dictionary<string, AICompletionReference>? References { get; init; }

        public string? ObservedUserMessage { get; private set; }

        public AICompletionContext? ObservedCompletionContext { get; private set; }

        public ValueTask<bool> CanHandleAsync(OrchestrationContextBuiltContext context) => ValueTask.FromResult(true);

        public Task HandleAsync(PreemptiveRagContext context)
        {
            ObservedUserMessage = context.OrchestrationContext.UserMessage;
            ObservedCompletionContext = context.OrchestrationContext.CompletionContext;

            if (Throws)
            {
                throw new InvalidOperationException("The index is unavailable.");
            }

            context.OrchestrationContext.SystemMessageBuilder.Append(_block);

            if (References is not null)
            {
                context.OrchestrationContext.Properties["DataSourceReferences"] = References;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSession : IRealtimeClientSession
    {
        public List<RealtimeClientMessage> Sent { get; } = [];

        public RealtimeSessionOptions? Options { get; set; }

        public Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);

            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;

            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
