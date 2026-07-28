using System.Security.Claims;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class FunctionInvocationAICompletionServiceHandlerTests
{
    [Fact]
    public async Task ConfigureAsync_StablyPrioritizesNonMcpEntriesAndPreservesDuplicatePrecedence()
    {
        var evaluator = new RecordingToolAccessEvaluator();
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
        var completionContext = new AICompletionContext();
        completionContext.AdditionalProperties[FunctionInvocationAICompletionServiceHandler.ScopedEntriesKey] = entries;
        var context = new CompletionServiceConfigureContext(new ChatOptions(), completionContext, true);
        var handler = new FunctionInvocationAICompletionServiceHandler(
            evaluator,
            new HttpContextAccessor(),
            new EmptyServiceProvider(),
            NullLogger<FunctionInvocationAICompletionServiceHandler>.Instance);

        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["local-first", "shared", "agent", "a2a", "local-last", "shared", "mcp-first", "mcp-second"],
            evaluator.ToolNames);
        Assert.Equal(expectedTools, context.ChatOptions.Tools);
    }

    [Fact]
    public async Task ConfigureAsync_SnapshotsEntriesBeforeInvokingFactories()
    {
        var evaluator = new RecordingToolAccessEvaluator();
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
        var completionContext = new AICompletionContext();
        completionContext.AdditionalProperties[FunctionInvocationAICompletionServiceHandler.ScopedEntriesKey] = entries;
        var context = new CompletionServiceConfigureContext(new ChatOptions(), completionContext, true);
        var handler = new FunctionInvocationAICompletionServiceHandler(
            evaluator,
            new HttpContextAccessor(),
            new EmptyServiceProvider(),
            NullLogger<FunctionInvocationAICompletionServiceHandler>.Instance);

        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(["first", "second"], evaluator.ToolNames);
        Assert.Equal([firstTool, secondTool], context.ChatOptions.Tools);
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public async Task ConfigureAsync_WhenToolsAreDenied_ExcludesThemAndLogsASingleWarning()
    {
        var evaluator = new RecordingToolAccessEvaluator
        {
            DeniedToolNames = { "denied-first", "denied-second" },
        };
        var allowedTool = new TestAIFunction("allowed-tool");
        IReadOnlyList<ToolRegistryEntry> entries =
        [
            CreateEntry("denied-first", "denied-first", ToolRegistryEntrySource.Local, new TestAIFunction("denied-first-tool")),
            CreateEntry("allowed", "allowed", ToolRegistryEntrySource.Local, allowedTool),
            CreateEntry("denied-second", "denied-second", ToolRegistryEntrySource.McpServer, new TestAIFunction("denied-second-tool")),
        ];
        var completionContext = new AICompletionContext();
        completionContext.AdditionalProperties[FunctionInvocationAICompletionServiceHandler.ScopedEntriesKey] = entries;
        var context = new CompletionServiceConfigureContext(new ChatOptions(), completionContext, true);
        var logger = new CapturingLogger<FunctionInvocationAICompletionServiceHandler>();
        var handler = new FunctionInvocationAICompletionServiceHandler(
            evaluator,
            new HttpContextAccessor(),
            new EmptyServiceProvider(),
            logger);

        await handler.ConfigureAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal([allowedTool], context.ChatOptions.Tools);

        var warning = Assert.Single(logger.Messages, message => message.Level == LogLevel.Warning);
        Assert.Contains("denied-first", warning.Message);
        Assert.Contains("denied-second", warning.Message);
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

        public Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, string toolName)
        {
            ToolNames.Add(toolName);

            return Task.FromResult(!DeniedToolNames.Contains(toolName));
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

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object GetService(Type serviceType)
        {
            return null;
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
