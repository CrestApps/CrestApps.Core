using System.Security.Claims;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the captured LINQ ordering path with production's stable tool-entry partition.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class FunctionInvocationToolOrderingBenchmarks
{
    private LegacyFunctionInvocationHandler _legacyHandler;
    private FunctionInvocationAICompletionServiceHandler _currentHandler;
    private CompletionServiceConfigureContext _legacyContext;
    private CompletionServiceConfigureContext _currentContext;

    /// <summary>
    /// Gets or sets the number of mixed-source tool entries.
    /// </summary>
    [Params(8, 64, 512)]
    public int EntryCount { get; set; }

    /// <summary>
    /// Creates equivalent mixed-source inputs and proves exact authorization and tool ordering.
    /// </summary>
    /// <returns>A task that completes after setup.</returns>
    [GlobalSetup]
    public async Task Setup()
    {
        var entries = CreateEntries(EntryCount);
        await VerifyEquivalenceAsync(entries);

        var evaluator = new AllowAllToolAccessEvaluator();
        var httpContextAccessor = new HttpContextAccessor();
        var serviceProvider = new EmptyServiceProvider();

        _legacyHandler = new LegacyFunctionInvocationHandler(
            evaluator,
            httpContextAccessor,
            serviceProvider,
            NullLogger<LegacyFunctionInvocationHandler>.Instance);
        _currentHandler = new FunctionInvocationAICompletionServiceHandler(
            evaluator,
            httpContextAccessor,
            serviceProvider,
            NullLogger<FunctionInvocationAICompletionServiceHandler>.Instance);
        _legacyContext = CreateContext(entries);
        _currentContext = CreateContext(entries);

        await _legacyHandler.ConfigureAsync(_legacyContext);
        await _currentHandler.ConfigureAsync(_currentContext);
    }

    /// <summary>
    /// Configures tools through the captured LINQ ordering path.
    /// </summary>
    /// <returns>A task that completes after configuration.</returns>
    [Benchmark(Baseline = true)]
    public Task Legacy()
    {
        _legacyContext.ChatOptions.Tools.Clear();

        return _legacyHandler.ConfigureAsync(_legacyContext);
    }

    /// <summary>
    /// Configures tools through production's stable partition.
    /// </summary>
    /// <returns>A task that completes after configuration.</returns>
    [Benchmark]
    public Task Current()
    {
        _currentContext.ChatOptions.Tools.Clear();

        return _currentHandler.ConfigureAsync(_currentContext);
    }

    private static List<ToolRegistryEntry> CreateEntries(int count)
    {
        var entries = new List<ToolRegistryEntry>(count);

        for (var index = 0; index < count; index++)
        {
            var tool = new TestAIFunction($"tool-{index}");
            var source = index % 2 == 0
                ? ToolRegistryEntrySource.McpServer
                : ((index / 2) % 4) switch
                {
                    0 => ToolRegistryEntrySource.Local,
                    1 => ToolRegistryEntrySource.System,
                    2 => ToolRegistryEntrySource.Agent,
                    _ => ToolRegistryEntrySource.A2AAgent,
                };

            entries.Add(new ToolRegistryEntry
            {
                Id = $"entry-{index}",
                Name = tool.Name,
                Source = source,
                CreateAsync = _ => new ValueTask<AITool>(tool),
            });
        }

        return entries;
    }

    private static CompletionServiceConfigureContext CreateContext(IReadOnlyList<ToolRegistryEntry> entries)
    {
        var completionContext = new AICompletionContext();
        completionContext.AdditionalProperties[FunctionInvocationAICompletionServiceHandler.ScopedEntriesKey] = entries;

        return new CompletionServiceConfigureContext(new ChatOptions(), completionContext, true);
    }

    private static async Task VerifyEquivalenceAsync(IReadOnlyList<ToolRegistryEntry> entries)
    {
        var legacyEvaluator = new RecordingToolAccessEvaluator();
        var currentEvaluator = new RecordingToolAccessEvaluator();
        var httpContextAccessor = new HttpContextAccessor();
        var serviceProvider = new EmptyServiceProvider();
        var legacy = new LegacyFunctionInvocationHandler(
            legacyEvaluator,
            httpContextAccessor,
            serviceProvider,
            NullLogger<LegacyFunctionInvocationHandler>.Instance);
        var current = new FunctionInvocationAICompletionServiceHandler(
            currentEvaluator,
            httpContextAccessor,
            serviceProvider,
            NullLogger<FunctionInvocationAICompletionServiceHandler>.Instance);
        var legacyContext = CreateContext(entries);
        var currentContext = CreateContext(entries);

        await legacy.ConfigureAsync(legacyContext);
        await current.ConfigureAsync(currentContext);

        if (!legacyEvaluator.ToolNames.SequenceEqual(currentEvaluator.ToolNames) ||
            !legacyContext.ChatOptions.Tools.SequenceEqual(currentContext.ChatOptions.Tools))
        {
            throw new InvalidOperationException("Function invocation ordering behavior differs.");
        }
    }

    private sealed class AllowAllToolAccessEvaluator : IAIToolAccessEvaluator
    {
        private static readonly Task<bool> _allowed = Task.FromResult(true);

        public Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, string toolName)
        {
            return _allowed;
        }
    }

    private sealed class RecordingToolAccessEvaluator : IAIToolAccessEvaluator
    {
        private static readonly Task<bool> _allowed = Task.FromResult(true);

        public List<string> ToolNames { get; } = [];

        public Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, string toolName)
        {
            ToolNames.Add(toolName);

            return _allowed;
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object GetService(Type serviceType)
        {
            return null;
        }
    }

    private sealed class TestAIFunction(string name) : AIFunction
    {
        public override string Name { get; } = name;

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

    private sealed class LegacyFunctionInvocationHandler
    {
        private readonly IAIToolAccessEvaluator _toolAccessEvaluator;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<LegacyFunctionInvocationHandler> _logger;

        public LegacyFunctionInvocationHandler(
            IAIToolAccessEvaluator toolAccessEvaluator,
            IHttpContextAccessor httpContextAccessor,
            IServiceProvider serviceProvider,
            ILogger<LegacyFunctionInvocationHandler> logger)
        {
            _toolAccessEvaluator = toolAccessEvaluator;
            _httpContextAccessor = httpContextAccessor;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task ConfigureAsync(
            CompletionServiceConfigureContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (!context.IsFunctionInvocationSupported ||
                context.CompletionContext is null ||
                    !context.CompletionContext.AdditionalProperties.TryGetValue(FunctionInvocationAICompletionServiceHandler.ScopedEntriesKey, out var entriesObj) ||
                        entriesObj is not IReadOnlyList<ToolRegistryEntry> scopedEntries ||
                            scopedEntries.Count == 0)
            {
                return;
            }

            context.ChatOptions.Tools ??= [];

            var user = _httpContextAccessor.HttpContext?.User;
            var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var orderedEntries = scopedEntries
                .OrderBy(entry => entry.Source == ToolRegistryEntrySource.McpServer ? 1 : 0);

            foreach (var entry in orderedEntries)
            {
                if (!await _toolAccessEvaluator.IsAuthorizedAsync(user, entry.Name))
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug(
                            "Tool '{ToolName}' from {Source} ({Id}) denied by access evaluator.",
                            entry.Name,
                            entry.Source,
                            entry.Id);
                    }

                    continue;
                }

                if (entry.CreateAsync is null)
                {
                    _logger.LogWarning("Tool entry '{ToolName}' ({Id}) has no ToolFactory. Skipping.", entry.Name, entry.Id);

                    continue;
                }

                if (!addedNames.Add(entry.Name))
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug(
                            "Skipping tool '{ToolName}' from {Source} ({Id}) ? name already registered.",
                            entry.Name,
                            entry.Source,
                            entry.Id);
                    }

                    continue;
                }

                try
                {
                    var tool = await entry.CreateAsync(_serviceProvider);

                    if (tool is not null)
                    {
                        context.ChatOptions.Tools.Add(tool);
                    }
                    else if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("ToolFactory returned null for '{ToolName}' ({Id}).", entry.Name, entry.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create tool '{ToolName}' ({Id}). Skipping.", entry.Name, entry.Id);
                }
            }
        }
    }
}
