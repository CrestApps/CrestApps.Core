using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the captured reverse-enumeration context builders with production's exact
/// per-phase array snapshots. This class must remain unsealed because BenchmarkDotNet
/// generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class ContextBuilderReverseDispatchBenchmarks
{
    private static readonly object _resource = new();
    private LegacyOrchestrationContextBuilder _legacyOrchestrationBuilder;
    private DefaultOrchestrationContextBuilder _currentOrchestrationBuilder;
    private LegacyAICompletionContextBuilder _legacyCompletionBuilder;
    private DefaultAICompletionContextBuilder _currentCompletionBuilder;

    /// <summary>
    /// Gets or sets the number of handlers dispatched in each build phase.
    /// </summary>
    [Params(0, 1, 4, 16, 64)]
    public int HandlerCount { get; set; }

    /// <summary>
    /// Creates independent legacy and production builders and verifies exact phase ordering.
    /// </summary>
    /// <returns>A task that completes after setup.</returns>
    [GlobalSetup]
    public async Task Setup()
    {
        await VerifyOrchestrationEquivalenceAsync();
        await VerifyCompletionEquivalenceAsync();

        var legacyOrchestrationHandlers = CreateOrchestrationHandlers();
        var currentOrchestrationHandlers = CreateOrchestrationHandlers();
        var serviceProvider = new EmptyServiceProvider();

        _legacyOrchestrationBuilder = new LegacyOrchestrationContextBuilder(
            legacyOrchestrationHandlers,
            serviceProvider,
            NullLogger<LegacyOrchestrationContextBuilder>.Instance);
        _currentOrchestrationBuilder = new DefaultOrchestrationContextBuilder(
            currentOrchestrationHandlers,
            serviceProvider,
            NullLogger<DefaultOrchestrationContextBuilder>.Instance);

        _legacyCompletionBuilder = new LegacyAICompletionContextBuilder(
            CreateCompletionHandlers(),
            NullLogger<LegacyAICompletionContextBuilder>.Instance);
        _currentCompletionBuilder = new DefaultAICompletionContextBuilder(
            CreateCompletionHandlers(),
            NullLogger<DefaultAICompletionContextBuilder>.Instance);
    }

    /// <summary>
    /// Builds an orchestration context with the captured implementation.
    /// </summary>
    /// <returns>The built orchestration context.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Orchestration")]
    public ValueTask<OrchestrationContext> LegacyOrchestration()
    {
        return _legacyOrchestrationBuilder.BuildAsync(_resource);
    }

    /// <summary>
    /// Builds an orchestration context with production.
    /// </summary>
    /// <returns>The built orchestration context.</returns>
    [Benchmark]
    [BenchmarkCategory("Orchestration")]
    public ValueTask<OrchestrationContext> CurrentOrchestration()
    {
        return _currentOrchestrationBuilder.BuildAsync(_resource);
    }

    /// <summary>
    /// Builds a completion context with the captured implementation.
    /// </summary>
    /// <returns>The built completion context.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Completion")]
    public ValueTask<AICompletionContext> LegacyCompletion()
    {
        return _legacyCompletionBuilder.BuildAsync(_resource);
    }

    /// <summary>
    /// Builds a completion context with production.
    /// </summary>
    /// <returns>The built completion context.</returns>
    [Benchmark]
    [BenchmarkCategory("Completion")]
    public ValueTask<AICompletionContext> CurrentCompletion()
    {
        return _currentCompletionBuilder.BuildAsync(_resource);
    }

    private IOrchestrationContextBuilderHandler[] CreateOrchestrationHandlers()
    {
        return Enumerable.Range(0, HandlerCount)
            .Select(_ => (IOrchestrationContextBuilderHandler)new NoOpOrchestrationHandler())
            .ToArray();
    }

    private IAICompletionContextBuilderHandler[] CreateCompletionHandlers()
    {
        return Enumerable.Range(0, HandlerCount)
            .Select(_ => (IAICompletionContextBuilderHandler)new NoOpCompletionHandler())
            .ToArray();
    }

    private async Task VerifyOrchestrationEquivalenceAsync()
    {
        var legacyTrace = new List<int>();
        var currentTrace = new List<int>();
        var serviceProvider = new EmptyServiceProvider();
        var legacyHandlers = Enumerable.Range(0, HandlerCount)
            .Select(index => (IOrchestrationContextBuilderHandler)new RecordingOrchestrationHandler(index, legacyTrace))
            .ToArray();
        var currentHandlers = Enumerable.Range(0, HandlerCount)
            .Select(index => (IOrchestrationContextBuilderHandler)new RecordingOrchestrationHandler(index, currentTrace))
            .ToArray();
        var legacy = new LegacyOrchestrationContextBuilder(
            legacyHandlers,
            serviceProvider,
            NullLogger<LegacyOrchestrationContextBuilder>.Instance);
        var current = new DefaultOrchestrationContextBuilder(
            currentHandlers,
            serviceProvider,
            NullLogger<DefaultOrchestrationContextBuilder>.Instance);

        var legacyContext = await legacy.BuildAsync(_resource);
        var currentContext = await current.BuildAsync(_resource);

        if (!legacyTrace.SequenceEqual(currentTrace) ||
            legacyContext.ServiceProvider != currentContext.ServiceProvider ||
            legacyContext.CompletionContext != currentContext.CompletionContext)
        {
            throw new InvalidOperationException("Orchestration builder behavior differs.");
        }
    }

    private async Task VerifyCompletionEquivalenceAsync()
    {
        var legacyTrace = new List<int>();
        var currentTrace = new List<int>();
        var legacyHandlers = Enumerable.Range(0, HandlerCount)
            .Select(index => (IAICompletionContextBuilderHandler)new RecordingCompletionHandler(index, legacyTrace))
            .ToArray();
        var currentHandlers = Enumerable.Range(0, HandlerCount)
            .Select(index => (IAICompletionContextBuilderHandler)new RecordingCompletionHandler(index, currentTrace))
            .ToArray();
        var legacy = new LegacyAICompletionContextBuilder(
            legacyHandlers,
            NullLogger<LegacyAICompletionContextBuilder>.Instance);
        var current = new DefaultAICompletionContextBuilder(
            currentHandlers,
            NullLogger<DefaultAICompletionContextBuilder>.Instance);

        var legacyContext = await legacy.BuildAsync(_resource);
        var currentContext = await current.BuildAsync(_resource);

        if (!legacyTrace.SequenceEqual(currentTrace) ||
            legacyContext.AdditionalProperties.Count != currentContext.AdditionalProperties.Count)
        {
            throw new InvalidOperationException("Completion builder behavior differs.");
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object GetService(Type serviceType)
        {
            return null;
        }
    }

    private sealed class NoOpOrchestrationHandler : IOrchestrationContextBuilderHandler
    {
        public Task BuildingAsync(
            OrchestrationContextBuildingContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task BuiltAsync(
            OrchestrationContextBuiltContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingOrchestrationHandler(
        int index,
        List<int> trace) : IOrchestrationContextBuilderHandler
    {
        public Task BuildingAsync(
            OrchestrationContextBuildingContext context,
            CancellationToken cancellationToken = default)
        {
            trace.Add(index);

            return Task.CompletedTask;
        }

        public Task BuiltAsync(
            OrchestrationContextBuiltContext context,
            CancellationToken cancellationToken = default)
        {
            trace.Add(index + 10_000);

            return Task.CompletedTask;
        }
    }

    private sealed class NoOpCompletionHandler : IAICompletionContextBuilderHandler
    {
        public Task BuildingAsync(AICompletionContextBuildingContext context)
        {
            return Task.CompletedTask;
        }

        public Task BuiltAsync(AICompletionContextBuiltContext context)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCompletionHandler(
        int index,
        List<int> trace) : IAICompletionContextBuilderHandler
    {
        public Task BuildingAsync(AICompletionContextBuildingContext context)
        {
            trace.Add(index);

            return Task.CompletedTask;
        }

        public Task BuiltAsync(AICompletionContextBuiltContext context)
        {
            trace.Add(index + 10_000);

            return Task.CompletedTask;
        }
    }

    private sealed class LegacyOrchestrationContextBuilder
    {
        private readonly IEnumerable<IOrchestrationContextBuilderHandler> _handlers;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<LegacyOrchestrationContextBuilder> _logger;

        public LegacyOrchestrationContextBuilder(
            IEnumerable<IOrchestrationContextBuilderHandler> handlers,
            IServiceProvider serviceProvider,
            ILogger<LegacyOrchestrationContextBuilder> logger)
        {
            _handlers = handlers?.Reverse() ?? [];
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async ValueTask<OrchestrationContext> BuildAsync(
            object resource,
            Action<OrchestrationContext> configure = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(resource);

            var context = new OrchestrationContext
            {
                ServiceProvider = _serviceProvider,
            };

            var building = new OrchestrationContextBuildingContext(resource, context);

            foreach (var handler in _handlers)
            {
                try
                {
                    await handler.BuildingAsync(building, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error in orchestration context building handler {Handler}.", handler.GetType().Name);
                }
            }

            configure?.Invoke(context);

            var built = new OrchestrationContextBuiltContext(resource, context);

            foreach (var handler in _handlers)
            {
                try
                {
                    await handler.BuiltAsync(built, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error in orchestration context built handler {Handler}.", handler.GetType().Name);
                }
            }

            if (context.CompletionContext != null && context.SystemMessageBuilder.Length > 0)
            {
                context.CompletionContext.SystemMessage = context.SystemMessageBuilder.ToString();
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var systemMessage = context.CompletionContext?.SystemMessage;

                if (!string.IsNullOrEmpty(systemMessage))
                {
                    _logger.LogDebug(
                        "Composed system message ({Length} chars) for resource type '{ResourceType}': {SystemMessage}",
                        systemMessage.Length,
                        resource.GetType().Name,
                        systemMessage);
                }
                else
                {
                    _logger.LogDebug("No system message composed for resource type '{ResourceType}'.", resource.GetType().Name);
                }
            }

            return context;
        }
    }

    private sealed class LegacyAICompletionContextBuilder
    {
        private readonly IEnumerable<IAICompletionContextBuilderHandler> _handlers;
        private readonly ILogger<LegacyAICompletionContextBuilder> _logger;

        public LegacyAICompletionContextBuilder(
            IEnumerable<IAICompletionContextBuilderHandler> handlers,
            ILogger<LegacyAICompletionContextBuilder> logger)
        {
            _handlers = handlers?.Reverse() ?? [];
            _logger = logger;
        }

        public async ValueTask<AICompletionContext> BuildAsync(
            object resource,
            Action<AICompletionContext> configure = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(resource);

            var context = new AICompletionContext();
            var building = new AICompletionContextBuildingContext(resource, context);

            foreach (var handler in _handlers)
            {
                try
                {
                    await handler.BuildingAsync(building);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error in completion context building handler {Handler}.", handler.GetType().Name);
                }
            }

            configure?.Invoke(context);

            var built = new AICompletionContextBuiltContext(resource, context);

            foreach (var handler in _handlers)
            {
                try
                {
                    await handler.BuiltAsync(built);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error in completion context built handler {Handler}.", handler.GetType().Name);
                }
            }

            return context;
        }
    }
}
