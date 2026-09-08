#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only.
#nullable enable
using System.Runtime.CompilerServices;
using System.Text.Json;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Realtime;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Realtime;

/// <summary>
/// Verifies that <see cref="DefaultRealtimeOrchestrator"/> wires the Microsoft.Extensions.AI realtime
/// function-invocation middleware correctly: a function call raised by the (fake) provider session is
/// resolved by invoking the profile's tool, the tool observes the ambient <see cref="AIInvocationScope"/>
/// and the request service provider, and a function result is sent back to the session.
/// </summary>
public sealed class DefaultRealtimeOrchestratorToolLoopTests
{
    [Fact]
    public async Task StartAsync_WhenSessionRaisesFunctionCall_InvokesToolUnderScopeAndReturnsResult()
    {
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var tool = new EchoTool();
        var fakeSession = new FakeRealtimeSession(
        [
            new ResponseOutputItemRealtimeServerMessage(RealtimeServerMessageType.ResponseOutputItemDone)
            {
                Item = new RealtimeConversationItem(
                    [new FunctionCallContent("call-1", tool.Name, new Dictionary<string, object?> { ["query"] = "hello" })],
                    id: "item-1"),
            },
        ]);
        var fakeClient = new FakeRealtimeClient(fakeSession);

        using var requestServices = new ServiceCollection().BuildServiceProvider();
        var orchestrator = CreateOrchestrator(profile, tool, fakeClient, requestServices);

        using var scope = AIInvocationScope.Begin();

        await using var conversation = await orchestrator.StartAsync(
            new RealtimeOrchestrationRequest { Resource = profile },
            TestContext.Current.CancellationToken);

        // Drain the event stream; enumerating it drives the realtime tool loop.
        await foreach (var _ in conversation.GetEventsAsync(TestContext.Current.CancellationToken))
        {
        }

        Assert.True(tool.Invoked);
        Assert.True(tool.ScopeWasPresent);
        Assert.Equal("ds-1", tool.CapturedDataSourceId);
        Assert.Same(requestServices, tool.CapturedServices);

        // The provider session advertised the tool...
        Assert.NotNull(fakeSession.Options?.Tools);
        Assert.Contains(fakeSession.Options!.Tools!, t => t is AIFunction f && f.Name == tool.Name);

        // ...and received the tool result back as a function_call_output.
        var result = fakeSession.SentMessages
            .SelectMany(ExtractContents)
            .OfType<FunctionResultContent>()
            .FirstOrDefault(r => r.CallId == "call-1");
        Assert.NotNull(result);
        Assert.Contains("TOOL_RESULT", result!.Result?.ToString());
    }

    [Fact]
    public async Task StartAsync_WithToolsButNoInvocationScope_Throws()
    {
        // Every tool call would come back as "requires an active AI execution context" in plain text, and the
        // model would relay that to the user as not knowing the answer. Fail where the cause is visible.
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var fakeClient = new FakeRealtimeClient(new FakeRealtimeSession([]));

        using var requestServices = new ServiceCollection().BuildServiceProvider();
        var orchestrator = CreateOrchestrator(profile, new EchoTool(), fakeClient, requestServices);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.StartAsync(
            new RealtimeOrchestrationRequest { Resource = profile },
            TestContext.Current.CancellationToken));

        Assert.Contains("AIInvocationScope", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_WhenGroundingIsAvailable_DefersResponseCreationToTheHost()
    {
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var session = new FakeRealtimeSession([]);
        var grounding = new FakeRealtimeTurnGrounding(available: true);

        using var requestServices = new ServiceCollection().BuildServiceProvider();
        var orchestrator = CreateOrchestrator(profile, new EchoTool(), new FakeRealtimeClient(session), requestServices, grounding);

        using var scope = AIInvocationScope.Begin();

        await using var conversation = await orchestrator.StartAsync(
            new RealtimeOrchestrationRequest { Resource = profile },
            TestContext.Current.CancellationToken);

        // The provider must not answer on its own; the host answers once it has retrieved for the turn.
        var turnDetection = Assert.IsType<RealtimeTurnDetectionOverrides>(session.Options!.RawRepresentationFactory!());
        Assert.False(turnDetection.CreateResponse);
        Assert.False(conversation.RespondsAutomatically);

        Assert.True(await conversation.GroundTurnAsync("what is the refund policy", TestContext.Current.CancellationToken));
        Assert.Equal("what is the refund policy", Assert.Single(grounding.Utterances));
    }

    [Fact]
    public async Task StartAsync_WhenGroundingIsUnavailable_LeavesTheProviderAnswering()
    {
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var session = new FakeRealtimeSession([]);

        using var requestServices = new ServiceCollection().BuildServiceProvider();
        var orchestrator = CreateOrchestrator(profile, new EchoTool(), new FakeRealtimeClient(session), requestServices);

        using var scope = AIInvocationScope.Begin();

        await using var conversation = await orchestrator.StartAsync(
            new RealtimeOrchestrationRequest { Resource = profile },
            TestContext.Current.CancellationToken);

        var turnDetection = Assert.IsType<RealtimeTurnDetectionOverrides>(session.Options!.RawRepresentationFactory!());
        Assert.True(turnDetection.CreateResponse);
        Assert.True(conversation.RespondsAutomatically);
    }

    [Fact]
    public async Task StartAsync_WithMoreToolsThanTheThreshold_ScopesThemAndKeepsTheKnowledgeSearch()
    {
        // Every tool sits in the model's context on every turn of a voice session, and selection accuracy falls
        // off as the list grows. Past chat's own threshold the set is trimmed — but never the knowledge search,
        // which the data-source handler marks as must-include.
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var search = new EchoTool();
        var session = new FakeRealtimeSession([]);
        var options = new DefaultOrchestratorOptions();

        var entries = Enumerable.Range(1, options.ScopingThreshold + 10)
            .Select(i => new ToolRegistryEntry { Id = $"mcp:conn:tool_{i}", Name = $"tool_{i}", Description = $"Unrelated capability {i}", Source = ToolRegistryEntrySource.McpServer })
            .Append(new ToolRegistryEntry { Id = search.Name, Name = search.Name, Source = ToolRegistryEntrySource.System })
            .ToList();

        using var requestServices = new ServiceCollection().BuildServiceProvider();
        var orchestrator = CreateOrchestrator(profile, search, new FakeRealtimeClient(session), requestServices, registryEntries: entries);

        using var scope = AIInvocationScope.Begin();

        await using var conversation = await orchestrator.StartAsync(
            new RealtimeOrchestrationRequest
            {
                Resource = profile,
                ConfigureContext = ctx => ctx.MustIncludeTools.Add(search.Name),
            },
            TestContext.Current.CancellationToken);

        var sessionTools = session.Options!.Tools!.OfType<AIFunction>().Select(f => f.Name).ToList();

        Assert.True(sessionTools.Count <= options.InitialToolCount + 1, $"Expected at most {options.InitialToolCount + 1} tools, got {sessionTools.Count}.");
        Assert.True(sessionTools.Count < entries.Count, "The set was not trimmed.");
        Assert.Contains(search.Name, sessionTools);
    }

    [Fact]
    public async Task StartAsync_WithinTheThreshold_PassesEveryToolThrough()
    {
        var profile = new AIProfile { Type = AIProfileType.Chat };
        var tool = new EchoTool();
        var session = new FakeRealtimeSession([]);
        var options = new DefaultOrchestratorOptions();

        var entries = Enumerable.Range(1, options.ScopingThreshold - 1)
            .Select(i => new ToolRegistryEntry { Id = $"t{i}", Name = $"tool_{i}", Source = ToolRegistryEntrySource.Local })
            .Append(new ToolRegistryEntry { Id = tool.Name, Name = tool.Name, Source = ToolRegistryEntrySource.System })
            .ToList();

        using var requestServices = new ServiceCollection().BuildServiceProvider();
        var orchestrator = CreateOrchestrator(profile, tool, new FakeRealtimeClient(session), requestServices, registryEntries: entries);

        using var scope = AIInvocationScope.Begin();

        await using var conversation = await orchestrator.StartAsync(
            new RealtimeOrchestrationRequest { Resource = profile },
            TestContext.Current.CancellationToken);

        Assert.Equal(entries.Count, session.Options!.Tools!.Count);
    }

    /// <summary>
    /// A function that exists only to be counted.
    /// </summary>
    private sealed class StubFunction : AIFunction
    {
        public StubFunction(string name) => Name = name;

        public override string Name { get; }

        public override string Description => "stub";

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
            => new("ok");
    }

    private static DefaultRealtimeOrchestrator CreateOrchestrator(
        AIProfile profile,
        AIFunction tool,
        IRealtimeClient client,
        IServiceProvider requestServices,
        IRealtimeTurnGrounding? grounding = null,
        IReadOnlyList<ToolRegistryEntry>? registryEntries = null)
    {
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext
            {
                SystemMessage = "You are a helpful realtime assistant.",
                DataSourceId = "ds-1",
            },
        };

        var contextBuilder = new FakeContextBuilder(context);

        var capabilityService = new Mock<IAIDeploymentCapabilityService>();
        capabilityService
            .Setup(c => c.ResolveDeploymentWithFeatureAsync(AIDeploymentFeatureNames.Realtime, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIDeployment { Name = "rt", ModelName = "gpt-realtime", ClientName = "test" });

        var deploymentSettings = new Mock<IOptionsMonitor<DefaultAIDeploymentSettings>>();
        deploymentSettings.Setup(s => s.CurrentValue).Returns(new DefaultAIDeploymentSettings());

        var clientFactory = new Mock<IAIClientFactory>();
        clientFactory
            .Setup(f => f.CreateRealtimeClientAsync(It.IsAny<AIDeployment>()))
            .Returns(new ValueTask<IRealtimeClient>(client));

        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry
            .Setup(r => r.GetAllAsync(It.IsAny<AICompletionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(registryEntries ?? [new ToolRegistryEntry { Id = tool.Name, Name = tool.Name, Source = ToolRegistryEntrySource.System }]);

        // Materialize one stub function per entry so the session's tool count mirrors what scoping let through.
        var materializer = new Mock<IToolMaterializer>();
        materializer
            .Setup(m => m.MaterializeAsync(It.IsAny<IReadOnlyList<ToolRegistryEntry>>(), It.IsAny<ToolMaterializationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ToolRegistryEntry> entries, ToolMaterializationOptions _, CancellationToken _) =>
                new ToolMaterializationResult
                {
                    Tools = entries.Select(entry => entry.Name == tool.Name ? tool : (AITool)new StubFunction(entry.Name)).ToList(),
                });

        return new DefaultRealtimeOrchestrator(
            contextBuilder,
            capabilityService.Object,
            deploymentSettings.Object,
            clientFactory.Object,
            toolRegistry.Object,
            materializer.Object,
            new DefaultRealtimeSessionConfigurator(),
            grounding ?? UngroundedTurnGrounding.Instance,
            new ToolRelevanceScoper(new LuceneTextTokenizer(), new DefaultOrchestratorOptions()),
            requestServices,
            NullLoggerFactory.Instance,
            NullLogger<DefaultRealtimeOrchestrator>.Instance);
    }

    private static IEnumerable<AIContent> ExtractContents(RealtimeClientMessage message)
    {
        return message switch
        {
            CreateConversationItemRealtimeClientMessage create => create.Item?.Contents ?? [],
            CreateResponseRealtimeClientMessage response => response.Items?.SelectMany(item => item.Contents ?? []) ?? [],
            _ => [],
        };
    }

    private sealed class FakeContextBuilder : IOrchestrationContextBuilder
    {
        private readonly OrchestrationContext _context;

        public FakeContextBuilder(OrchestrationContext context)
        {
            _context = context;
        }

        public ValueTask<OrchestrationContext> BuildAsync(object resource, Action<OrchestrationContext>? configure = null, CancellationToken cancellationToken = default)
        {
            // Mirror the real builder: run the caller configuration, and set the tool execution context on
            // the ambient scope the way AIToolExecutionContextOrchestrationHandler does.
            configure?.Invoke(_context);

            if (AIInvocationScope.Current is { } invocation)
            {
                invocation.ToolExecutionContext ??= new AIToolExecutionContext(resource);
            }

            return ValueTask.FromResult(_context);
        }
    }

    private sealed class EchoTool : AIFunction
    {
        public bool Invoked { get; private set; }

        public bool ScopeWasPresent { get; private set; }

        public string? CapturedDataSourceId { get; private set; }

        public IServiceProvider? CapturedServices { get; private set; }

        public override string Name => "echo_tool";

        public override string Description => "Echoes the query back.";

        public override JsonElement JsonSchema => JsonSerializer.Deserialize<JsonElement>(
            """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}""");

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            Invoked = true;
            CapturedServices = arguments.Services;

            var scope = AIInvocationScope.Current;
            ScopeWasPresent = scope is not null;
            CapturedDataSourceId = scope?.DataSourceId;

            return new ValueTask<object?>("TOOL_RESULT");
        }
    }

    private sealed class FakeRealtimeClient : IRealtimeClient
    {
        private readonly FakeRealtimeSession _session;

        public FakeRealtimeClient(FakeRealtimeSession session)
        {
            _session = session;
        }

        public Task<IRealtimeClientSession> CreateSessionAsync(RealtimeSessionOptions? options = null, CancellationToken cancellationToken = default)
        {
            _session.Configure(options);

            return Task.FromResult<IRealtimeClientSession>(_session);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }

    private sealed class FakeRealtimeSession : IRealtimeClientSession
    {
        private readonly IReadOnlyList<RealtimeServerMessage> _script;

        public FakeRealtimeSession(IReadOnlyList<RealtimeServerMessage> script)
        {
            _script = script;
        }

        public List<RealtimeClientMessage> SentMessages { get; } = [];

        public RealtimeSessionOptions? Options { get; private set; }

        public void Configure(RealtimeSessionOptions? options) => Options = options;

        public Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken = default)
        {
            SentMessages.Add(message);

            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var message in _script)
            {
                cancellationToken.ThrowIfCancellationRequested();

                yield return message;

                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
