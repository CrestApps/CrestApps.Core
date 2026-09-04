using System.Security.Claims;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Security;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class FunctionInvocationAICompletionServiceHandlerTests
{
    [Fact]
    public async Task ConfigureAsync_StablyPrioritizesNonMcpEntriesAndPreservesDuplicatePrecedence()
    {
        var expectedTools = new AITool[]
        {
            new TestAIFunction("local-first-tool"),
            new TestAIFunction("system-shared-tool"),
            new TestAIFunction("agent-tool"),
            new TestAIFunction("a2a-tool"),
            new TestAIFunction("local-last-tool"),
            new TestAIFunction("mcp-first-tool"),
            new TestAIFunction("mcp-second-tool"),
        };
        IReadOnlyList<ToolRegistryEntry> entries =
        [
            CreateEntry("mcp-shared", "shared", ToolRegistryEntrySource.McpServer, new TestAIFunction("mcp-shared-tool")),
            CreateEntry("local-first", "local-first", ToolRegistryEntrySource.Local, expectedTools[0]),
            CreateEntry("mcp-first", "mcp-first", ToolRegistryEntrySource.McpServer, expectedTools[5]),
            CreateEntry("system-shared", "shared", ToolRegistryEntrySource.System, expectedTools[1]),
            CreateEntry("agent", "agent", ToolRegistryEntrySource.Agent, expectedTools[2]),
            CreateEntry("mcp-second", "mcp-second", ToolRegistryEntrySource.McpServer, expectedTools[6]),
            CreateEntry("a2a", "a2a", ToolRegistryEntrySource.A2AAgent, expectedTools[3]),
            CreateEntry("local-last", "local-last", ToolRegistryEntrySource.Local, expectedTools[4]),
        ];
        var context = CreateContext(entries);
        var handler = CreateHandler();

        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(expectedTools, context.ChatOptions.Tools);
    }

    [Fact]
    public async Task ConfigureAsync_SnapshotsEntriesBeforeInvokingFactories()
    {
        var firstTool = new TestAIFunction("first-tool");
        var secondTool = new TestAIFunction("second-tool");
        List<ToolRegistryEntry> entries = null;
        var firstEntry = CreateEntry("first", "first", ToolRegistryEntrySource.Local, firstTool);
        firstEntry.CreateAsync = _ =>
        {
            entries.Add(CreateEntry("added", "added", ToolRegistryEntrySource.Local, new TestAIFunction("added-tool")));

            return new ValueTask<AITool>(firstTool);
        };
        entries =
        [
            CreateEntry("second", "second", ToolRegistryEntrySource.McpServer, secondTool),
            firstEntry,
        ];
        var context = CreateContext(entries);
        var handler = CreateHandler();

        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal([firstTool, secondTool], context.ChatOptions.Tools);
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public async Task ConfigureAsync_ForAISession_KeepsEveryToolWithoutConsultingEvaluator()
    {
        // An AI Session runs the profile as configured. The access evaluator must not be consulted,
        // even for a listable tool it would deny.
        var evaluator = new RecordingToolAccessEvaluator { DeniedToolNames = { "listable" } };
        var listableTool = new TestAIFunction("listable-tool");
        IReadOnlyList<ToolRegistryEntry> entries =
        [
            CreateEntry("listable", "listable", ToolRegistryEntrySource.Local, listableTool),
        ];
        var context = CreateContext(entries); // No Interaction marker => AI Session.
        var handler = CreateHandler(
            evaluator,
            new TestUserAccessor(),
            CreateToolDefinitions(services => services.AddCoreAITool<RegistrationTestTool>("listable").Selectable()));

        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(evaluator.ToolNames);
        Assert.Equal([listableTool], context.ChatOptions.Tools);
    }

    [Fact]
    public async Task ConfigureAsync_ForChatInteraction_ExcludesDeniedListableToolsAndLogsWarning()
    {
        var evaluator = new RecordingToolAccessEvaluator { DeniedToolNames = { "denied" } };
        var allowedTool = new TestAIFunction("allowed-tool");
        IReadOnlyList<ToolRegistryEntry> entries =
        [
            CreateEntry("denied", "denied", ToolRegistryEntrySource.Local, new TestAIFunction("denied-tool")),
            CreateEntry("allowed", "allowed", ToolRegistryEntrySource.Local, allowedTool),
        ];
        var context = CreateContext(entries, isChatInteraction: true);
        var logger = new CapturingLogger<FunctionInvocationAICompletionServiceHandler>();
        var handler = CreateHandler(
            evaluator,
            new TestUserAccessor(),
            CreateToolDefinitions(services =>
            {
                services.AddCoreAITool<RegistrationTestTool>("denied").Selectable();
                services.AddCoreAITool<RegistrationTestTool>("allowed").Selectable();
            }),
            logger);

        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal([allowedTool], context.ChatOptions.Tools);
        Assert.Equal(["denied", "allowed"], evaluator.ToolNames);

        var warning = Assert.Single(logger.Messages, message => message.Level == LogLevel.Warning);
        Assert.Contains("denied", warning.Message);
    }

    [Fact]
    public async Task ConfigureAsync_ForChatInteraction_DoesNotGateSystemHiddenOrMcpTools()
    {
        // The evaluator denies everything, but only listable tools are checked.
        var evaluator = new RecordingToolAccessEvaluator { DenyAll = true };
        var systemTool = new TestAIFunction("system-tool");
        var hiddenTool = new TestAIFunction("hidden-tool");
        var mcpTool = new TestAIFunction("mcp-tool");
        var unregisteredTool = new TestAIFunction("unregistered-tool");
        IReadOnlyList<ToolRegistryEntry> entries =
        [
            CreateEntry("system", "system", ToolRegistryEntrySource.System, systemTool),
            CreateEntry("hidden", "hidden", ToolRegistryEntrySource.Local, hiddenTool),
            CreateEntry("mcp", "mcp", ToolRegistryEntrySource.McpServer, mcpTool),
            CreateEntry("unregistered", "unregistered", ToolRegistryEntrySource.Local, unregisteredTool),
        ];
        var context = CreateContext(entries, isChatInteraction: true);
        var handler = CreateHandler(
            evaluator,
            new TestUserAccessor(),
            CreateToolDefinitions(services =>
            {
                services.AddCoreAITool<RegistrationTestTool>("system"); // System tool by default.
                services.AddCoreAITool<RegistrationTestTool>("hidden").Hidden();
            }));

        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        // None of these are listable, so the deny-all evaluator never removes them.
        Assert.Equal([systemTool, hiddenTool, unregisteredTool, mcpTool], context.ChatOptions.Tools);
        Assert.Empty(evaluator.ToolNames);
    }

    [Fact]
    public async Task ConfigureAsync_ForChatInteraction_WithNullCaller_KeepsEveryTool()
    {
        var evaluator = new RecordingToolAccessEvaluator { DenyAll = true };
        var listableTool = new TestAIFunction("listable-tool");
        IReadOnlyList<ToolRegistryEntry> entries =
        [
            CreateEntry("listable", "listable", ToolRegistryEntrySource.Local, listableTool),
        ];
        var context = CreateContext(entries, isChatInteraction: true);
        var handler = CreateHandler(
            evaluator,
            new TestUserAccessor(user: null),
            CreateToolDefinitions(services => services.AddCoreAITool<RegistrationTestTool>("listable").Selectable()));

        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(evaluator.ToolNames);
        Assert.Equal([listableTool], context.ChatOptions.Tools);
    }

    private static FunctionInvocationAICompletionServiceHandler CreateHandler(
        IAIToolAccessEvaluator evaluator = null,
        IUserAccessor userAccessor = null,
        IOptions<AIToolDefinitionOptions> toolDefinitions = null,
        ILogger<FunctionInvocationAICompletionServiceHandler> logger = null)
    {
        var materializer = new DefaultToolMaterializer(
            evaluator ?? new RecordingToolAccessEvaluator(),
            toolDefinitions ?? CreateToolDefinitions(_ => { }),
            new EmptyServiceProvider(),
            NullLogger<DefaultToolMaterializer>.Instance);

        return new FunctionInvocationAICompletionServiceHandler(
            materializer,
            userAccessor ?? new TestUserAccessor(),
            logger ?? NullLogger<FunctionInvocationAICompletionServiceHandler>.Instance);
    }

    private static CompletionServiceConfigureContext CreateContext(
        IReadOnlyList<ToolRegistryEntry> entries,
        bool isChatInteraction = false)
    {
        var completionContext = new AICompletionContext();
        completionContext.AdditionalProperties[FunctionInvocationAICompletionServiceHandler.ScopedEntriesKey] = entries;

        if (isChatInteraction)
        {
            completionContext.AdditionalProperties[AICompletionContextKeys.Interaction] = new object();
        }

        return new CompletionServiceConfigureContext(new ChatOptions(), completionContext, true);
    }

    private static IOptions<AIToolDefinitionOptions> CreateToolDefinitions(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();

        services.AddOptions();
        configure(services);

        using var serviceProvider = services.BuildServiceProvider();

        return Options.Create(serviceProvider.GetRequiredService<IOptions<AIToolDefinitionOptions>>().Value);
    }

    private static ToolRegistryEntry CreateEntry(
        string id,
        string name,
        ToolRegistryEntrySource source,
        AITool tool)
    {
        return new ToolRegistryEntry
        {
            Id = id,
            Name = name,
            Source = source,
            CreateAsync = _ => new ValueTask<AITool>(tool),
        };
    }

    private sealed class RecordingToolAccessEvaluator : IAIToolAccessEvaluator
    {
        public List<string> ToolNames { get; } = [];

        public HashSet<string> DeniedToolNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool DenyAll { get; set; }

        public Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, string toolName)
        {
            ToolNames.Add(toolName);

            return Task.FromResult(!DenyAll && !DeniedToolNames.Contains(toolName));
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            Messages.Add((logLevel, formatter(state, exception)));
        }
    }

    private sealed class TestUserAccessor : IUserAccessor
    {
        public TestUserAccessor()
            : this(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-user")], "Test")))
        {
        }

        public TestUserAccessor(ClaimsPrincipal user)
        {
            User = user;
        }

        public ClaimsPrincipal User { get; set; }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object GetService(Type serviceType)
        {
            return null;
        }
    }

    private sealed class RegistrationTestTool : AIFunction
    {
        public override string Name => "registration-test-tool";

        public override string Description => Name;

        public override System.Text.Json.JsonElement JsonSchema
        {
            get
            {
                return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{}");
            }
        }

        protected override ValueTask<object> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            return new ValueTask<object>(Name);
        }
    }

    private sealed class TestAIFunction : AIFunction
    {
        public TestAIFunction(string name)
        {
            Name = name;
        }

        public override string Name { get; }

        public override string Description => Name;

        public override System.Text.Json.JsonElement JsonSchema
        {
            get
            {
                return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{}");
            }
        }

        protected override ValueTask<object> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            return new ValueTask<object>(Name);
        }
    }
}
