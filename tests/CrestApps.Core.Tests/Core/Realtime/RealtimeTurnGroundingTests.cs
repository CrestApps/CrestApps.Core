#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only.
#nullable enable
using System.Text.Json;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Realtime;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

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
    public void Configure_WithoutAChosenLanguage_TellsTheModelToMirrorTheUser()
    {
        // "Automatic" on the client sends no language. The model still must not drift on its own, so the directive
        // mirrors the user's language instead of pinning a locale that may not be the one they are speaking.
        var options = new DefaultRealtimeSessionConfigurator().Configure(new RealtimeSessionConfiguratorContext
        {
            Model = "gpt-realtime",
            Instructions = "You answer questions about the knowledge base.",
        });

        Assert.StartsWith("Always speak and respond in the same language the user is speaking", options.Instructions);
        Assert.Contains("Do not switch to another language on your own", options.Instructions);
        Assert.EndsWith("You answer questions about the knowledge base.", options.Instructions);

        // No language hint reaches transcription: the transcriber keeps auto-detecting.
        Assert.Null(options.TranscriptionOptions?.SpeechLanguage);
    }

    [Fact]
    public void Configure_WithAReplyLanguageButNoChosenOne_PinsTheReplyAndLeavesTranscriptionAuto()
    {
        // The browser locale pins what the model says, not what it hears: a bilingual user with an English browser
        // speaking Spanish is still transcribed correctly, and answered in English as their browser asks.
        var options = new DefaultRealtimeSessionConfigurator().Configure(new RealtimeSessionConfiguratorContext
        {
            Model = "gpt-realtime",
            Instructions = "You answer questions about the knowledge base.",
            ReplyLanguage = "en-US",
        });

        Assert.StartsWith("Always speak and respond in English.", options.Instructions);
        Assert.Null(options.TranscriptionOptions?.SpeechLanguage);
    }

    [Fact]
    public void Configure_WithAChosenLanguage_ItWinsOverTheReplyLanguage()
    {
        var options = new DefaultRealtimeSessionConfigurator().Configure(new RealtimeSessionConfiguratorContext
        {
            Model = "gpt-realtime",
            SpeechLanguage = "es",
            ReplyLanguage = "en-US",
        });

        Assert.StartsWith("Always speak and respond in Spanish.", options.Instructions);
        Assert.Equal("es", options.TranscriptionOptions?.SpeechLanguage);
    }

    [Theory]
    [InlineData("en-US,en;q=0.9,vi;q=0.8", "en-US")]
    [InlineData("fr", "fr")]
    [InlineData("*", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("xx-invalid-zz,de;q=0.5", "de")]
    public void ReplyLanguage_FromAcceptLanguage_TakesTheFirstRealCulture(string? header, string? expected)
    {
        Assert.Equal(expected, CrestApps.Core.AI.Chat.Realtime.RealtimeReplyLanguage.FromAcceptLanguage(header));
    }

    [Fact]
    public void ReplyLanguage_Resolve_PrefersTheExplicitLanguage()
    {
        Assert.Equal("es", CrestApps.Core.AI.Chat.Realtime.RealtimeReplyLanguage.Resolve(" es ", httpContext: null));
        Assert.Null(CrestApps.Core.AI.Chat.Realtime.RealtimeReplyLanguage.Resolve(null, httpContext: null));
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
    public async Task RetrieveAsync_InScopeProfileWithNoResults_StillReturnsTheStayInScopeGuidance()
    {
        // "Restrict answers to retrieved data only" with nothing retrieved used to inject nothing, and the model
        // answered from its own knowledge — the one thing a restricted profile must never do.
        var profile = new AIProfile();
        profile.Put(new AIDataSourceRagMetadata { IsInScope = true });
        var grounding = CreateGrounding(new RecordingPreemptiveRagHandler(string.Empty), preemptiveEnabled: true);

        using var scope = AIInvocationScope.Begin();

        var retrieved = await grounding.RetrieveAsync(CreateContext(dataSourceId: "ds-1"), profile, "what is RAG", TestContext.Current.CancellationToken);

        Assert.NotNull(retrieved);
        Assert.Contains("GUIDANCE:" + AITemplateIds.RagScopeNoRefsToolsEnabled, retrieved);
    }

    [Fact]
    public async Task RetrieveAsync_InScopeProfileWithResults_AppendsTheWithinRetrievedDataGuidance()
    {
        var profile = new AIProfile();
        profile.Put(new AIDataSourceRagMetadata { IsInScope = true });
        var handler = new RecordingPreemptiveRagHandler("KNOWLEDGE BLOCK")
        {
            References = new Dictionary<string, AICompletionReference> { ["[doc:1]"] = new() { Index = 1, ReferenceId = "kb-1" } },
        };
        var grounding = CreateGrounding(handler, preemptiveEnabled: true);

        using var scope = AIInvocationScope.Begin();

        var retrieved = await grounding.RetrieveAsync(CreateContext(dataSourceId: "ds-1"), profile, "what is RAG", TestContext.Current.CancellationToken);

        Assert.StartsWith("KNOWLEDGE BLOCK", retrieved);
        Assert.Contains("GUIDANCE:" + AITemplateIds.RagScopeWithRefs, retrieved);
    }

    [Fact]
    public async Task RetrieveAsync_ProfileNotInScopeWithNoResults_ReturnsNothing()
    {
        // Out of scope, the model may use its own knowledge, and the response guidelines already sit in the
        // session instructions; there is nothing per-turn to add.
        var grounding = CreateGrounding(new RecordingPreemptiveRagHandler(string.Empty), preemptiveEnabled: true);

        using var scope = AIInvocationScope.Begin();

        var retrieved = await grounding.RetrieveAsync(CreateContext(dataSourceId: "ds-1"), new AIProfile(), "hello", TestContext.Current.CancellationToken);

        Assert.Null(retrieved);
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
    public void IsGroundingAvailable_WhenTheVoiceOnlySwitchIsOff_IsOff()
    {
        // Lets a host keep text grounded while leaving voice as quick to start speaking as possible.
        var grounding = CreateGrounding(new RecordingPreemptiveRagHandler("x"), preemptiveEnabled: true, voiceEnabled: false);

        Assert.False(grounding.IsGroundingAvailable(CreateContext(dataSourceId: "ds-1"), new AIProfile()));
    }

    [Fact]
    public async Task RequestAcknowledgementAsync_SpeaksOutOfBandWithoutToolsOrHistory()
    {
        var session = new RecordingSession();
        var conversation = new DefaultRealtimeConversation(session, (_, _) => Task.FromResult<string?>(null));

        await conversation.RequestAcknowledgementAsync("Say one short filler sentence.", TestContext.Current.CancellationToken);

        var message = Assert.IsType<CreateResponseRealtimeClientMessage>(Assert.Single(session.Sent));
        Assert.Equal("Say one short filler sentence.", message.Instructions);

        // Never added to the conversation: the model must not later see itself having said "let me look that up"
        // and treat it as an answer already begun.
        Assert.True(message.ExcludeFromConversation);

        // A filler that searched would defeat its own purpose, and one that ran long would still be talking when
        // the answer is ready.
        Assert.IsType<NoneChatToolMode>(message.ToolMode);
        Assert.NotNull(message.MaxOutputTokens);
    }

    [Fact]
    public async Task RequestAcknowledgementAsync_OnAnAutomaticSession_IsANoOp()
    {
        var session = new RecordingSession();
        var conversation = new DefaultRealtimeConversation(session);

        await conversation.RequestAcknowledgementAsync("Say something.", TestContext.Current.CancellationToken);

        Assert.Empty(session.Sent);
    }

    [Fact]
    public async Task RequestResponseAsync_DoesNotOverrideTheSessionTools()
    {
        // The answer must be able to call every tool the session advertised — the search tool included, so the
        // model can go looking for more than retrieval handed it. Writing tools or a tool choice onto this
        // response would replace the session's for that turn.
        var session = new RecordingSession();
        var conversation = new DefaultRealtimeConversation(session, (_, _) => Task.FromResult<string?>(null));

        await conversation.RequestResponseAsync(TestContext.Current.CancellationToken);

        var message = Assert.IsType<CreateResponseRealtimeClientMessage>(Assert.Single(session.Sent));
        Assert.Null(message.Tools);
        Assert.Null(message.ToolMode);
        Assert.Null(message.Instructions);
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

    private static DefaultRealtimeTurnGrounding CreateGrounding(IPreemptiveRagHandler handler, bool preemptiveEnabled, bool voiceEnabled = true)
        => CreateGrounding([handler], preemptiveEnabled, voiceEnabled);

    private static DefaultRealtimeTurnGrounding CreateGrounding(IPreemptiveRagHandler[] handlers, bool preemptiveEnabled, bool voiceEnabled = true)
    {
        return new DefaultRealtimeTurnGrounding(
            handlers,
            CreateTemplateService(),
            new TestOptionsMonitor<DefaultOrchestratorSettings> { CurrentValue = new DefaultOrchestratorSettings { EnablePreemptiveRag = preemptiveEnabled } },
            Options.Create(new RealtimeTransportOptions { EnableKnowledgeGrounding = voiceEnabled }),
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

    /// <summary>
    /// Renders every template as a marker carrying its id, so a test can assert which guidance was chosen.
    /// </summary>
    private static CrestApps.Core.Templates.Services.ITemplateService CreateTemplateService()
    {
        var templates = new Mock<CrestApps.Core.Templates.Services.ITemplateService>();
        templates
            .Setup(t => t.RenderAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, IDictionary<string, object>? _, CancellationToken _) => "GUIDANCE:" + id);

        return templates.Object;
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
