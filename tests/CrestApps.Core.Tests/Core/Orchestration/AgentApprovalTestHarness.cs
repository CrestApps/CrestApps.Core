using System.Runtime.CompilerServices;
using System.Text.Json;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Security;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Orchestration;

/// <summary>
/// Runs an assistant that delegates to an agent through the real pipeline (the default orchestrator, the tool
/// registry, the tool materializer, a named completion client and the function-invoking chat client), with a
/// scripted model in place of a provider. The agent has a tool that reads (<c>list_items</c>) and a destructive tool
/// (<c>delete_item</c>), which requires an approval because of its kind, so an approval policy can lower it.
/// </summary>
internal sealed class AgentApprovalTestHarness : IDisposable
{
    public const string AgentName = "content-agent";
    public const string AgentCallId = "agent-call-1";
    public const string ListCallId = "list-1";
    public const string DeleteCallId = "delete-1";
    public const string SecondDeleteCallId = "delete-2";
    public const string Prompt = "Delete item 7";

    private readonly ServiceProvider _services;

    public AgentApprovalTestHarness()
    {
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddCoreAITool<ListItemsTool>(ListItemsTool.TheName).Selectable();
        services.AddCoreAITool<DeleteItemTool>(DeleteItemTool.TheName).AsDestructive().Selectable();

        var agent = new AIProfile
        {
            ItemId = "agent-1",
            Name = AgentName,
            Description = "Lists and deletes items.",
            Type = AIProfileType.Agent,
        };
        agent.Put(new AgentMetadata { AllowToolInvocation = true });

        var profiles = new Mock<IAIProfileManager>();
        profiles.Setup(manager => manager.GetAsync(AIProfileType.Agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync([agent]);

        var deployment = new AIDeployment
        {
            ItemId = "deployment-1",
            Name = "chat",
            ClientName = TestProvider.ClientName,
            ModelName = "test-model",
        };
        var deployments = new Mock<IAIDeploymentManager>();
        deployments.Setup(manager => manager.ResolveSlotAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(deployment);

        var clientFactory = new Mock<IAIClientFactory>();
        clientFactory.Setup(factory => factory.CreateChatClientAsync(It.IsAny<AIDeployment>()))
            .ReturnsAsync(() => Model);

        services.AddSingleton(profiles.Object);
        services.AddSingleton(deployments.Object);
        services.AddSingleton(clientFactory.Object);
        services.AddSingleton<IToolMaterializer>(provider => new DefaultToolMaterializer(
            Mock.Of<IAIToolAccessEvaluator>(),
            provider.GetRequiredService<IOptions<AIToolDefinitionOptions>>(),
            provider,
            NullLogger<DefaultToolMaterializer>.Instance));
        services.AddSingleton<IAICompletionService>(provider => new CompletionService(new ProviderAICompletionClient<TestProvider>(
            provider.GetRequiredService<IAIClientFactory>(),
            NullLoggerFactory.Instance,
            Mock.Of<IDistributedCache>(),
            provider,
            [
                new FunctionInvocationAICompletionServiceHandler(
                    provider.GetRequiredService<IToolMaterializer>(),
                    Mock.Of<IUserAccessor>(),
                    NullLogger<FunctionInvocationAICompletionServiceHandler>.Instance),
            ],
            new DefaultAIOptions { EnableDistributedCaching = false },
            Mock.Of<ITemplateService>(),
            provider.GetRequiredService<IAIDeploymentManager>())));
        services.AddSingleton<IToolRegistry>(provider => new DefaultToolRegistry(
            [
                new AgentToolRegistryProvider(provider.GetRequiredService<IAIProfileManager>()),
                new ProfileToolRegistryProvider(provider.GetRequiredService<IOptions<AIToolDefinitionOptions>>()),
            ],
            provider.GetRequiredService<IOptions<AIToolDefinitionOptions>>(),
            new LuceneTextTokenizer(),
            NullLogger<DefaultToolRegistry>.Instance));
        services.AddSingleton(provider => new DefaultOrchestrator(
            provider.GetRequiredService<IAICompletionService>(),
            provider.GetRequiredService<IAIClientFactory>(),
            Mock.Of<ITemplateService>(),
            provider.GetRequiredService<IAIDeploymentManager>(),
            provider.GetRequiredService<IToolRegistry>(),
            new LuceneTextTokenizer(),
            Options.Create(new DefaultOrchestratorOptions()),
            NullLogger<DefaultOrchestrator>.Instance));
        services.AddSingleton<IOrchestratorResolver>(provider => new OrchestratorResolver(provider.GetRequiredService<DefaultOrchestrator>()));
        services.AddSingleton<IOrchestrationContextBuilder>(AgentContexts);

        _services = services.BuildServiceProvider();
    }

    /// <summary>
    /// Gets the scripted model both the assistant and the agent run on.
    /// </summary>
    public ScriptedModel Model { get; } = new();

    /// <summary>
    /// Gets the builder that makes the agent's orchestration context, and keeps each context it built.
    /// </summary>
    public AgentContextBuilder AgentContexts { get; } = new();

    public IServiceProvider Services => _services;

    public DefaultOrchestrator Orchestrator => _services.GetRequiredService<DefaultOrchestrator>();

    public ListItemsTool ListTool => _services.GetRequiredService<ListItemsTool>();

    public DeleteItemTool DeleteTool => _services.GetRequiredService<DeleteItemTool>();

    /// <summary>
    /// Gets or sets a value indicating whether the assistant's profile was set to run its tools without asking.
    /// </summary>
    public bool AssistantRunsToolsWithoutAsking { get; set; }

    /// <summary>
    /// Creates the approval policy of a profile set to run every tool kind that requires an approval without asking.
    /// </summary>
    /// <returns>The policy.</returns>
    public static ToolApprovalPolicy CreateAllowAllPolicy()
    {
        var settings = new AIToolApprovalSettings();
        settings.SetRunAllWithoutAsking(true);

        return ToolApprovalPolicy.FromSettings(settings);
    }

    /// <summary>
    /// Creates the orchestration context of the assistant's turn: a profile that has the agent and no tools of
    /// its own.
    /// </summary>
    /// <param name="history">The conversation history, ending with what the turn answers.</param>
    /// <param name="supportsApproval">Whether the caller can ask a person to approve a tool call.</param>
    /// <returns>The orchestration context.</returns>
    public OrchestrationContext CreateAssistantContext(IList<ChatMessage> history, bool supportsApproval)
    {
        var completionContext = new AICompletionContext
        {
            ChatDeploymentName = "chat",
            AgentNames = [AgentName],
        };

        if (supportsApproval)
        {
            completionContext.AdditionalProperties[AICompletionContextKeys.ToolApprovalSupported] = true;
        }

        if (AssistantRunsToolsWithoutAsking)
        {
            completionContext.SetToolApprovalPolicy(CreateAllowAllPolicy());
        }

        return new OrchestrationContext
        {
            UserMessage = Prompt,
            ConversationHistory = history,
            CompletionContext = completionContext,
            ServiceProvider = _services,
        };
    }

    /// <summary>
    /// Runs the assistant's turn inside the given invocation scope, the way the chat response handler does, and
    /// returns every update of its stream.
    /// </summary>
    /// <param name="invocation">The invocation context of the run.</param>
    /// <param name="history">The conversation history, ending with what the turn answers.</param>
    /// <param name="supportsApproval">Whether the caller can ask a person to approve a tool call.</param>
    /// <param name="onUpdate">Called with each update, before the next one is read.</param>
    /// <returns>The updates of the stream.</returns>
    public async Task<List<ChatResponseUpdate>> RunAssistantAsync(
        AIInvocationContext invocation,
        IList<ChatMessage> history,
        bool supportsApproval,
        Func<ChatResponseUpdate, ValueTask> onUpdate = null)
    {
        var context = CreateAssistantContext(history, supportsApproval);
        var updates = new List<ChatResponseUpdate>();

        invocation.CompletionContext = context.CompletionContext;

        await foreach (var update in Orchestrator.ExecuteStreamingAsync(context, TestContext.Current.CancellationToken))
        {
            updates.Add(update);

            if (onUpdate is not null)
            {
                await onUpdate(update);
            }
        }

        return updates;
    }

    public void Dispose()
    {
        _services.Dispose();
    }

    /// <summary>
    /// The model of the assistant and of the agent. The assistant hands the task to the agent and then repeats
    /// what the agent answered. The agent lists the items, deletes item 7, and reports what the delete returned.
    /// Each step is decided from the tool results in the messages, as a model does, so a run that resumes with its
    /// earlier work in the messages goes on from there and a run that lost it starts again.
    /// </summary>
    public sealed class ScriptedModel : IChatClient
    {
        private int _responses;

        /// <summary>
        /// Gets the messages of each request the assistant's model received.
        /// </summary>
        public List<List<ChatMessage>> AssistantRequests { get; } = [];

        /// <summary>
        /// Gets the messages of each request the agent's model received.
        /// </summary>
        public List<List<ChatMessage>> AgentRequests { get; } = [];

        /// <summary>
        /// Gets or sets a value indicating whether the agent deletes item 8 after item 7, in a second call that
        /// needs an approval of its own.
        /// </summary>
        public bool DeletesTwoItems { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the agent lists the items and deletes item 7 in one round of tool
        /// calls, instead of listing first.
        /// </summary>
        public bool ListsAndDeletesInOneRound { get; set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(Answer(messages.ToList(), options)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            var answer = Answer(messages.ToList(), options);

            yield return new ChatResponseUpdate(answer.Role, answer.Contents)
            {
                MessageId = answer.MessageId,
                ResponseId = answer.MessageId,
            };
        }

        public object GetService(Type serviceType, object serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
        }

        private ChatMessage Answer(List<ChatMessage> messages, ChatOptions options)
        {
            var isAssistant = options?.Tools?.Any(tool => tool.Name == AgentName) == true;
            var contents = isAssistant
                ? AnswerAsAssistant(messages)
                : AnswerAsAgent(messages);

            (isAssistant ? AssistantRequests : AgentRequests).Add(messages);

            return new ChatMessage(ChatRole.Assistant, contents)
            {
                MessageId = $"response-{Interlocked.Increment(ref _responses)}",
            };
        }

        private static List<AIContent> AnswerAsAssistant(List<ChatMessage> messages)
        {
            var agentResult = FindResult(messages, AgentCallId);

            if (agentResult is null)
            {
                return [new FunctionCallContent(AgentCallId, AgentName, new Dictionary<string, object> { ["prompt"] = Prompt })];
            }

            return [new TextContent($"The agent answered: {agentResult}")];
        }

        private List<AIContent> AnswerAsAgent(List<ChatMessage> messages)
        {
            if (ListsAndDeletesInOneRound && FindResult(messages, ListCallId) is null && FindResult(messages, DeleteCallId) is null)
            {
                return
                [
                    new FunctionCallContent(ListCallId, ListItemsTool.TheName, new Dictionary<string, object>()),
                    new FunctionCallContent(DeleteCallId, DeleteItemTool.TheName, new Dictionary<string, object> { ["id"] = 7 }),
                ];
            }

            if (FindResult(messages, ListCallId) is null)
            {
                return [new FunctionCallContent(ListCallId, ListItemsTool.TheName, new Dictionary<string, object>())];
            }

            var deleteResult = FindResult(messages, DeleteCallId);

            if (deleteResult is null)
            {
                return [new FunctionCallContent(DeleteCallId, DeleteItemTool.TheName, new Dictionary<string, object> { ["id"] = 7 })];
            }

            if (!DeletesTwoItems)
            {
                return [new TextContent($"The delete tool answered: {deleteResult}")];
            }

            var secondDeleteResult = FindResult(messages, SecondDeleteCallId);

            if (secondDeleteResult is null)
            {
                return [new FunctionCallContent(SecondDeleteCallId, DeleteItemTool.TheName, new Dictionary<string, object> { ["id"] = 8 })];
            }

            return [new TextContent($"The delete tool answered: {deleteResult} Then: {secondDeleteResult}")];
        }

        private static string FindResult(List<ChatMessage> messages, string callId)
        {
            return messages
                .SelectMany(message => message.Contents)
                .OfType<FunctionResultContent>()
                .FirstOrDefault(result => result.CallId == callId)
                ?.Result
                ?.ToString();
        }
    }

    /// <summary>
    /// Builds the agent's orchestration context: the two tools of the agent's profile.
    /// </summary>
    public sealed class AgentContextBuilder : IOrchestrationContextBuilder
    {
        /// <summary>
        /// Gets each context the builder made, in order.
        /// </summary>
        public List<OrchestrationContext> Built { get; } = [];

        /// <summary>
        /// Gets or sets a value indicating whether the agent's profile was set to run its tools without asking.
        /// </summary>
        public bool RunsToolsWithoutAsking { get; set; }

        public ValueTask<OrchestrationContext> BuildAsync(object resource, Action<OrchestrationContext> configure = null, CancellationToken cancellationToken = default)
        {
            var context = new OrchestrationContext
            {
                CompletionContext = new AICompletionContext
                {
                    ChatDeploymentName = "chat",
                    ToolNames = [ListItemsTool.TheName, DeleteItemTool.TheName],
                },
            };

            if (RunsToolsWithoutAsking)
            {
                // The agent profile's own policy, as the profile handler sets it before configure runs.
                context.CompletionContext.SetToolApprovalPolicy(CreateAllowAllPolicy());
            }

            configure?.Invoke(context);
            Built.Add(context);

            return ValueTask.FromResult(context);
        }
    }

    public sealed class ListItemsTool : AIFunction
    {
        public const string TheName = "list_items";

        private int _callCount;

        public int CallCount => _callCount;

        public override string Name => TheName;

        public override string Description => "Lists the items.";

        public override JsonElement JsonSchema { get; } = JsonElement.Parse("""{"type":"object","properties":{}}""");

        protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);

            return ValueTask.FromResult<object>("Items: 7, 8");
        }
    }

    public sealed class DeleteItemTool : AIFunction
    {
        public const string TheName = "delete_item";

        private int _callCount;

        public int CallCount => _callCount;

        public override string Name => TheName;

        public override string Description => "Deletes an item.";

        public override JsonElement JsonSchema { get; } = JsonElement.Parse("""{"type":"object","properties":{"id":{"type":"integer"}}}""");

        protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);

            arguments.TryGetValue("id", out var id);

            return ValueTask.FromResult<object>($"Deleted item {id}.");
        }
    }

    private sealed class CompletionService : IAICompletionService
    {
        private readonly IAICompletionClient _client;

        public CompletionService(IAICompletionClient client)
        {
            _client = client;
        }

        public Task<ChatResponse> CompleteAsync(AIDeployment deployment, IEnumerable<ChatMessage> messages, AICompletionContext context, CancellationToken cancellationToken = default)
        {
            return _client.CompleteAsync(messages, context, cancellationToken);
        }

        public IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingAsync(AIDeployment deployment, IEnumerable<ChatMessage> messages, AICompletionContext context, CancellationToken cancellationToken = default)
        {
            return _client.CompleteStreamingAsync(messages, context, cancellationToken);
        }
    }

    private sealed class OrchestratorResolver : IOrchestratorResolver
    {
        private readonly IOrchestrator _orchestrator;

        public OrchestratorResolver(IOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        public IOrchestrator Resolve(string orchestratorName = null)
        {
            return _orchestrator;
        }
    }

    private sealed class TestProvider : IAIClientMarker
    {
        public static string ClientName => "test-provider";
    }
}
