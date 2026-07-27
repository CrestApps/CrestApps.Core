using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Exceptions;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Defines completion-handler outcomes used by the dispatch benchmarks.
/// </summary>
public enum CompletionHandlerOutcome
{
    /// <summary>
    /// Every handler completes successfully.
    /// </summary>
    Successful,

    /// <summary>
    /// Every handler observes the context and returns the same cached faulted task.
    /// </summary>
    Faulting,
}

/// <summary>
/// Compares the captured completion-handler dispatch implementation with production through
/// in-memory streaming and non-streaming clients. Operation counts normalize streaming results
/// to nanoseconds and allocated bytes per update. This class must remain unsealed because
/// BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class DefaultAICompletionServiceHandlerDispatchBenchmarks
{
    private CompletionDispatchScenario _oneUpdateScenario;
    private CompletionDispatchScenario _thirtyTwoUpdateScenario;
    private CompletionDispatchScenario _twoHundredFiftySixUpdateScenario;
    private CompletionDispatchScenario _fourThousandNinetySixUpdateScenario;
    private CompletionDispatchScenario _nonStreamingScenario;

    /// <summary>
    /// Gets or sets the number of completion handlers.
    /// </summary>
    [Params(0, 1, 4)]
    public int HandlerCount { get; set; }

    /// <summary>
    /// Gets or sets whether handlers complete successfully or fault.
    /// </summary>
    [Params(CompletionHandlerOutcome.Successful, CompletionHandlerOutcome.Faulting)]
    public CompletionHandlerOutcome Outcome { get; set; }

    /// <summary>
    /// Creates independent legacy and production services and proves exact output and observation equivalence.
    /// </summary>
    /// <returns>A task that completes after all equivalence checks.</returns>
    [GlobalSetup]
    public async Task Setup()
    {
        _oneUpdateScenario = new CompletionDispatchScenario(1, HandlerCount, Outcome);
        _thirtyTwoUpdateScenario = new CompletionDispatchScenario(32, HandlerCount, Outcome);
        _twoHundredFiftySixUpdateScenario = new CompletionDispatchScenario(256, HandlerCount, Outcome);
        _fourThousandNinetySixUpdateScenario = new CompletionDispatchScenario(4096, HandlerCount, Outcome);
        _nonStreamingScenario = new CompletionDispatchScenario(0, HandlerCount, Outcome);

        await _oneUpdateScenario.VerifyStreamingEquivalenceAsync();
        await _thirtyTwoUpdateScenario.VerifyStreamingEquivalenceAsync();
        await _twoHundredFiftySixUpdateScenario.VerifyStreamingEquivalenceAsync();
        await _fourThousandNinetySixUpdateScenario.VerifyStreamingEquivalenceAsync();
        await _nonStreamingScenario.VerifyNonStreamingEquivalenceAsync();
    }

    /// <summary>
    /// Releases the service providers created for this parameter combination.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _oneUpdateScenario.Dispose();
        _thirtyTwoUpdateScenario.Dispose();
        _twoHundredFiftySixUpdateScenario.Dispose();
        _fourThousandNinetySixUpdateScenario.Dispose();
        _nonStreamingScenario.Dispose();
    }

    /// <summary>
    /// Streams one update with the captured implementation.
    /// </summary>
    /// <returns>The emitted-update checksum.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = 1)]
    [BenchmarkCategory("Streaming-1")]
    public Task<int> LegacyStreaming1()
    {
        return _oneUpdateScenario.RunLegacyStreamingAsync();
    }

    /// <summary>
    /// Streams one update with the production implementation.
    /// </summary>
    /// <returns>The emitted-update checksum.</returns>
    [Benchmark(OperationsPerInvoke = 1)]
    [BenchmarkCategory("Streaming-1")]
    public Task<int> CurrentStreaming1()
    {
        return _oneUpdateScenario.RunCurrentStreamingAsync();
    }

    /// <summary>
    /// Streams 32 updates with the captured implementation.
    /// </summary>
    /// <returns>The emitted-update checksum.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = 32)]
    [BenchmarkCategory("Streaming-32")]
    public Task<int> LegacyStreaming32()
    {
        return _thirtyTwoUpdateScenario.RunLegacyStreamingAsync();
    }

    /// <summary>
    /// Streams 32 updates with the production implementation.
    /// </summary>
    /// <returns>The emitted-update checksum.</returns>
    [Benchmark(OperationsPerInvoke = 32)]
    [BenchmarkCategory("Streaming-32")]
    public Task<int> CurrentStreaming32()
    {
        return _thirtyTwoUpdateScenario.RunCurrentStreamingAsync();
    }

    /// <summary>
    /// Streams 256 updates with the captured implementation.
    /// </summary>
    /// <returns>The emitted-update checksum.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = 256)]
    [BenchmarkCategory("Streaming-256")]
    public Task<int> LegacyStreaming256()
    {
        return _twoHundredFiftySixUpdateScenario.RunLegacyStreamingAsync();
    }

    /// <summary>
    /// Streams 256 updates with the production implementation.
    /// </summary>
    /// <returns>The emitted-update checksum.</returns>
    [Benchmark(OperationsPerInvoke = 256)]
    [BenchmarkCategory("Streaming-256")]
    public Task<int> CurrentStreaming256()
    {
        return _twoHundredFiftySixUpdateScenario.RunCurrentStreamingAsync();
    }

    /// <summary>
    /// Streams 4,096 updates with the captured implementation.
    /// </summary>
    /// <returns>The emitted-update checksum.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = 4096)]
    [BenchmarkCategory("Streaming-4096")]
    public Task<int> LegacyStreaming4096()
    {
        return _fourThousandNinetySixUpdateScenario.RunLegacyStreamingAsync();
    }

    /// <summary>
    /// Streams 4,096 updates with the production implementation.
    /// </summary>
    /// <returns>The emitted-update checksum.</returns>
    [Benchmark(OperationsPerInvoke = 4096)]
    [BenchmarkCategory("Streaming-4096")]
    public Task<int> CurrentStreaming4096()
    {
        return _fourThousandNinetySixUpdateScenario.RunCurrentStreamingAsync();
    }

    /// <summary>
    /// Completes one non-streaming response with the captured implementation.
    /// </summary>
    /// <returns>The response identity checksum.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("NonStreaming")]
    public Task<int> LegacyNonStreaming()
    {
        return _nonStreamingScenario.RunLegacyNonStreamingAsync();
    }

    /// <summary>
    /// Completes one non-streaming response with the production implementation.
    /// </summary>
    /// <returns>The response identity checksum.</returns>
    [Benchmark]
    [BenchmarkCategory("NonStreaming")]
    public Task<int> CurrentNonStreaming()
    {
        return _nonStreamingScenario.RunCurrentNonStreamingAsync();
    }

    /// <summary>
    /// Owns one pair of legacy/current services and their immutable in-memory inputs.
    /// </summary>
    private sealed class CompletionDispatchScenario : IDisposable
    {
        private const string ClientName = "benchmark-client";
        private static readonly AIDeployment _deployment = new()
        {
            Name = "benchmark-deployment",
            ClientName = ClientName,
        };
        private static readonly ChatMessage[] _messages =
        [
            new(ChatRole.User, "benchmark"),
        ];
        private readonly InMemoryCompletionClient _client;
        private readonly DefaultAICompletionService _currentService;
        private readonly RecordingCompletionHandler[] _currentHandlers;
        private readonly CountingLogger _currentLogger;
        private readonly LegacyDefaultAICompletionService _legacyService;
        private readonly RecordingCompletionHandler[] _legacyHandlers;
        private readonly CountingLogger _legacyLogger;
        private readonly ServiceProvider _serviceProvider;
        private readonly ChatResponseUpdate[] _updates;

        /// <summary>
        /// Initializes a new dispatch scenario.
        /// </summary>
        /// <param name="updateCount">The number of streamed updates.</param>
        /// <param name="handlerCount">The number of handlers.</param>
        /// <param name="outcome">The handler outcome.</param>
        public CompletionDispatchScenario(
            int updateCount,
            int handlerCount,
            CompletionHandlerOutcome outcome)
        {
            _updates = CreateUpdates(updateCount);
            _client = new InMemoryCompletionClient(_updates);
            var services = new ServiceCollection();
            services.AddSingleton(_client);
            services.AddCoreAICompletionClient<InMemoryCompletionClient>(ClientName);
            _serviceProvider = services.BuildServiceProvider();
            var options = _serviceProvider.GetRequiredService<IOptions<AIOptions>>();
            _legacyHandlers = CreateHandlers(handlerCount, outcome);
            _currentHandlers = CreateHandlers(handlerCount, outcome);
            _legacyLogger = new CountingLogger();
            _currentLogger = new CountingLogger();
            _legacyService = new LegacyDefaultAICompletionService(
                _serviceProvider,
                _legacyHandlers,
                options,
                _legacyLogger);
            _currentService = new DefaultAICompletionService(
                _serviceProvider,
                _currentHandlers,
                options,
                _currentLogger);
        }

        /// <summary>
        /// Proves streamed output identity, handler observation order and identity, cancellation, and logging equivalence.
        /// </summary>
        /// <returns>A task that completes after equivalence verification.</returns>
        public async Task VerifyStreamingEquivalenceAsync()
        {
            SetCapture(_legacyHandlers, true);
            SetCapture(_currentHandlers, true);

            var legacyUpdates = await CollectAsync(_legacyService.CompleteStreamingAsync(
                _deployment,
                _messages,
                new AICompletionContext()));
            var currentUpdates = await CollectAsync(_currentService.CompleteStreamingAsync(
                _deployment,
                _messages,
                new AICompletionContext()));

            EnsureEmittedUpdatesEquivalent(_updates, legacyUpdates, currentUpdates);
            EnsureStreamingObservationsEquivalent(
                _updates,
                _legacyHandlers,
                _currentHandlers);
            EnsureLogCountsEquivalent(
                _legacyLogger,
                _currentLogger,
                _updates.Length,
                _legacyHandlers);
            Reset();
        }

        /// <summary>
        /// Proves non-streaming response identity, handler observation order and identity, cancellation, and logging equivalence.
        /// </summary>
        /// <returns>A task that completes after equivalence verification.</returns>
        public async Task VerifyNonStreamingEquivalenceAsync()
        {
            SetCapture(_legacyHandlers, true);
            SetCapture(_currentHandlers, true);

            var legacyResponse = await _legacyService.CompleteAsync(
                _deployment,
                _messages,
                new AICompletionContext());
            var currentResponse = await _currentService.CompleteAsync(
                _deployment,
                _messages,
                new AICompletionContext());

            if (!ReferenceEquals(_client.Response, legacyResponse) ||
                !ReferenceEquals(_client.Response, currentResponse))
            {
                throw new InvalidOperationException("Completion implementations returned different response references.");
            }

            EnsureNonStreamingObservationsEquivalent(
                _client.Response,
                _legacyHandlers,
                _currentHandlers);
            EnsureLogCountsEquivalent(
                _legacyLogger,
                _currentLogger,
                operationCount: 1,
                _legacyHandlers);
            Reset();
        }

        /// <summary>
        /// Runs the captured streaming implementation.
        /// </summary>
        /// <returns>The emitted-update checksum.</returns>
        public Task<int> RunLegacyStreamingAsync()
        {
            return ConsumeAsync(_legacyService.CompleteStreamingAsync(
                _deployment,
                _messages,
                new AICompletionContext()));
        }

        /// <summary>
        /// Runs the production streaming implementation.
        /// </summary>
        /// <returns>The emitted-update checksum.</returns>
        public Task<int> RunCurrentStreamingAsync()
        {
            return ConsumeAsync(_currentService.CompleteStreamingAsync(
                _deployment,
                _messages,
                new AICompletionContext()));
        }

        /// <summary>
        /// Runs the captured non-streaming implementation.
        /// </summary>
        /// <returns>The response identity checksum.</returns>
        public async Task<int> RunLegacyNonStreamingAsync()
        {
            var response = await _legacyService.CompleteAsync(
                _deployment,
                _messages,
                new AICompletionContext());

            return RuntimeHelpers.GetHashCode(response);
        }

        /// <summary>
        /// Runs the production non-streaming implementation.
        /// </summary>
        /// <returns>The response identity checksum.</returns>
        public async Task<int> RunCurrentNonStreamingAsync()
        {
            var response = await _currentService.CompleteAsync(
                _deployment,
                _messages,
                new AICompletionContext());

            return RuntimeHelpers.GetHashCode(response);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _serviceProvider.Dispose();
        }

        /// <summary>
        /// Creates stable update instances.
        /// </summary>
        /// <param name="count">The update count.</param>
        /// <returns>The updates.</returns>
        private static ChatResponseUpdate[] CreateUpdates(int count)
        {
            var updates = new ChatResponseUpdate[count];

            for (var index = 0; index < updates.Length; index++)
            {
                updates[index] = new ChatResponseUpdate(ChatRole.Assistant, $"update-{index}");
            }

            return updates;
        }

        /// <summary>
        /// Creates handlers with independent observation buffers.
        /// </summary>
        /// <param name="handlerCount">The handler count.</param>
        /// <param name="outcome">The handler outcome.</param>
        /// <returns>The handlers.</returns>
        private static RecordingCompletionHandler[] CreateHandlers(
            int handlerCount,
            CompletionHandlerOutcome outcome)
        {
            var handlers = new RecordingCompletionHandler[handlerCount];

            for (var index = 0; index < handlers.Length; index++)
            {
                handlers[index] = new RecordingCompletionHandler(index, outcome);
            }

            return handlers;
        }

        /// <summary>
        /// Enables or disables detailed setup-only observations.
        /// </summary>
        /// <param name="handlers">The handlers.</param>
        /// <param name="capture">Whether to capture.</param>
        private static void SetCapture(
            RecordingCompletionHandler[] handlers,
            bool capture)
        {
            foreach (var handler in handlers)
            {
                handler.Capture = capture;
            }
        }

        /// <summary>
        /// Collects emitted updates for setup-only equivalence verification.
        /// </summary>
        /// <param name="updates">The update stream.</param>
        /// <returns>The emitted updates.</returns>
        private static async Task<List<ChatResponseUpdate>> CollectAsync(
            IAsyncEnumerable<ChatResponseUpdate> updates)
        {
            var result = new List<ChatResponseUpdate>();

            await foreach (var update in updates)
            {
                result.Add(update);
            }

            return result;
        }

        /// <summary>
        /// Consumes a stream without allocating a result collection.
        /// </summary>
        /// <param name="updates">The update stream.</param>
        /// <returns>The emitted-update checksum.</returns>
        private static async Task<int> ConsumeAsync(
            IAsyncEnumerable<ChatResponseUpdate> updates)
        {
            var checksum = 17;

            await foreach (var update in updates)
            {
                checksum = unchecked((checksum * 31) + RuntimeHelpers.GetHashCode(update));
            }

            return checksum;
        }

        /// <summary>
        /// Verifies both implementations emit the same source references in the same order.
        /// </summary>
        /// <param name="expected">The source updates.</param>
        /// <param name="legacy">The captured implementation updates.</param>
        /// <param name="current">The production implementation updates.</param>
        private static void EnsureEmittedUpdatesEquivalent(
            ChatResponseUpdate[] expected,
            List<ChatResponseUpdate> legacy,
            List<ChatResponseUpdate> current)
        {
            if (legacy.Count != expected.Length ||
                current.Count != expected.Length)
            {
                throw new InvalidOperationException("Completion implementations emitted different update counts.");
            }

            for (var index = 0; index < expected.Length; index++)
            {
                if (!ReferenceEquals(expected[index], legacy[index]) ||
                    !ReferenceEquals(expected[index], current[index]))
                {
                    throw new InvalidOperationException(
                        $"Completion implementations emitted different update references at index {index}.");
                }
            }
        }

        /// <summary>
        /// Verifies per-update handler order, shared context identity, update identity, and default handler tokens.
        /// </summary>
        /// <param name="updates">The source updates.</param>
        /// <param name="legacyHandlers">The captured implementation handlers.</param>
        /// <param name="currentHandlers">The production implementation handlers.</param>
        private static void EnsureStreamingObservationsEquivalent(
            ChatResponseUpdate[] updates,
            RecordingCompletionHandler[] legacyHandlers,
            RecordingCompletionHandler[] currentHandlers)
        {
            var expectedObservationCount = updates.Length * legacyHandlers.Length;
            var legacy = FlattenObservations(legacyHandlers);
            var current = FlattenObservations(currentHandlers);

            if (legacy.Count != expectedObservationCount ||
                current.Count != expectedObservationCount)
            {
                throw new InvalidOperationException("Completion handlers observed different update counts.");
            }

            for (var updateIndex = 0; updateIndex < updates.Length; updateIndex++)
            {
                object legacyContext = null;
                object currentContext = null;

                for (var handlerIndex = 0; handlerIndex < legacyHandlers.Length; handlerIndex++)
                {
                    var observationIndex = (updateIndex * legacyHandlers.Length) + handlerIndex;
                    var legacyObservation = legacy[observationIndex];
                    var currentObservation = current[observationIndex];

                    if (legacyObservation.HandlerIndex != handlerIndex ||
                        currentObservation.HandlerIndex != handlerIndex ||
                        !ReferenceEquals(legacyObservation.Payload, updates[updateIndex]) ||
                        !ReferenceEquals(currentObservation.Payload, updates[updateIndex]) ||
                        legacyObservation.CancellationToken.CanBeCanceled ||
                        currentObservation.CancellationToken.CanBeCanceled)
                    {
                        throw new InvalidOperationException(
                            $"Completion handlers observed different streamed values at index {observationIndex}.");
                    }

                    legacyContext ??= legacyObservation.Context;
                    currentContext ??= currentObservation.Context;

                    if (!ReferenceEquals(legacyContext, legacyObservation.Context) ||
                        !ReferenceEquals(currentContext, currentObservation.Context))
                    {
                        throw new InvalidOperationException(
                            $"Completion handlers did not share one context for update {updateIndex}.");
                    }
                }

                if (updateIndex > 0 &&
                    legacyHandlers.Length > 0)
                {
                    var priorObservationIndex = ((updateIndex - 1) * legacyHandlers.Length);

                    if (ReferenceEquals(legacy[priorObservationIndex].Context, legacyContext) ||
                        ReferenceEquals(current[priorObservationIndex].Context, currentContext))
                    {
                        throw new InvalidOperationException("Completion handlers reused a context across updates.");
                    }
                }
            }
        }

        /// <summary>
        /// Verifies handler order, shared context identity, response identity, and default handler tokens.
        /// </summary>
        /// <param name="response">The source response.</param>
        /// <param name="legacyHandlers">The captured implementation handlers.</param>
        /// <param name="currentHandlers">The production implementation handlers.</param>
        private static void EnsureNonStreamingObservationsEquivalent(
            ChatResponse response,
            RecordingCompletionHandler[] legacyHandlers,
            RecordingCompletionHandler[] currentHandlers)
        {
            var legacy = FlattenObservations(legacyHandlers);
            var current = FlattenObservations(currentHandlers);

            if (legacy.Count != legacyHandlers.Length ||
                current.Count != currentHandlers.Length)
            {
                throw new InvalidOperationException("Completion handlers observed different response counts.");
            }

            object legacyContext = null;
            object currentContext = null;

            for (var handlerIndex = 0; handlerIndex < legacyHandlers.Length; handlerIndex++)
            {
                var legacyObservation = legacy[handlerIndex];
                var currentObservation = current[handlerIndex];

                if (legacyObservation.HandlerIndex != handlerIndex ||
                    currentObservation.HandlerIndex != handlerIndex ||
                    !ReferenceEquals(legacyObservation.Payload, response) ||
                    !ReferenceEquals(currentObservation.Payload, response) ||
                    legacyObservation.CancellationToken.CanBeCanceled ||
                    currentObservation.CancellationToken.CanBeCanceled)
                {
                    throw new InvalidOperationException(
                        $"Completion handlers observed different response values at index {handlerIndex}.");
                }

                legacyContext ??= legacyObservation.Context;
                currentContext ??= currentObservation.Context;

                if (!ReferenceEquals(legacyContext, legacyObservation.Context) ||
                    !ReferenceEquals(currentContext, currentObservation.Context))
                {
                    throw new InvalidOperationException("Completion handlers did not share one response context.");
                }
            }
        }

        /// <summary>
        /// Flattens observations into invocation order.
        /// </summary>
        /// <param name="handlers">The handlers.</param>
        /// <returns>The ordered observations.</returns>
        private static List<HandlerObservation> FlattenObservations(
            RecordingCompletionHandler[] handlers)
        {
            var count = handlers.Sum(static handler => handler.Observations.Count);
            var result = new List<HandlerObservation>(count);

            if (handlers.Length == 0)
            {
                return result;
            }

            var operationCount = handlers[0].Observations.Count;

            for (var operationIndex = 0; operationIndex < operationCount; operationIndex++)
            {
                for (var handlerIndex = 0; handlerIndex < handlers.Length; handlerIndex++)
                {
                    result.Add(handlers[handlerIndex].Observations[operationIndex]);
                }
            }

            return result;
        }

        /// <summary>
        /// Verifies fault logging counts are equivalent and match the configured outcome.
        /// </summary>
        /// <param name="legacyLogger">The captured implementation logger.</param>
        /// <param name="currentLogger">The production implementation logger.</param>
        /// <param name="operationCount">The update or response count.</param>
        /// <param name="handlers">The configured handlers.</param>
        private static void EnsureLogCountsEquivalent(
            CountingLogger legacyLogger,
            CountingLogger currentLogger,
            int operationCount,
            RecordingCompletionHandler[] handlers)
        {
            var expectedLogCount = handlers.Length > 0 &&
                handlers[0].Outcome == CompletionHandlerOutcome.Faulting
                    ? operationCount * handlers.Length
                    : 0;

            if (legacyLogger.LogCount != expectedLogCount ||
                currentLogger.LogCount != expectedLogCount)
            {
                throw new InvalidOperationException(
                    $"Completion implementations logged {legacyLogger.LogCount} and {currentLogger.LogCount} errors; expected {expectedLogCount}.");
            }
        }

        /// <summary>
        /// Disables setup-only capture and clears counters before measurement.
        /// </summary>
        private void Reset()
        {
            foreach (var handler in _legacyHandlers)
            {
                handler.Reset();
            }

            foreach (var handler in _currentHandlers)
            {
                handler.Reset();
            }

            _legacyLogger.Reset();
            _currentLogger.Reset();
        }
    }

    /// <summary>
    /// Captures the production completion service before dispatch optimization.
    /// </summary>
    private sealed class LegacyDefaultAICompletionService
    {
        private readonly AIOptions _aiOptions;
        private readonly IEnumerable<IAICompletionHandler> _completionHandlers;
        private readonly ILogger<DefaultAICompletionService> _logger;
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// Initializes a new captured completion service.
        /// </summary>
        /// <param name="serviceProvider">The service provider.</param>
        /// <param name="completionHandlers">The completion handlers.</param>
        /// <param name="aiOptions">The AI options.</param>
        /// <param name="logger">The logger.</param>
        public LegacyDefaultAICompletionService(
            IServiceProvider serviceProvider,
            IEnumerable<IAICompletionHandler> completionHandlers,
            IOptions<AIOptions> aiOptions,
            ILogger<DefaultAICompletionService> logger)
        {
            _serviceProvider = serviceProvider;
            _completionHandlers = completionHandlers;
            _aiOptions = aiOptions.Value;
            _logger = logger;
        }

        /// <summary>
        /// Completes a non-streaming response with the captured handler dispatch.
        /// </summary>
        /// <param name="deployment">The deployment.</param>
        /// <param name="messages">The messages.</param>
        /// <param name="context">The completion context.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The completion response.</returns>
        public async Task<ChatResponse> CompleteAsync(
            AIDeployment deployment,
            IEnumerable<ChatMessage> messages,
            AICompletionContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(deployment);
            ArgumentNullException.ThrowIfNull(messages);
            ArgumentNullException.ThrowIfNull(context);

            var client = ResolveClient(deployment);

            var response = await client.CompleteAsync(messages, context, cancellationToken)
                ?? throw new InvalidOperationException("Unable to generate a response. Ensure that the connection, and the deployment names are correct.");

            var updateContext = new ReceivedMessageContext(response);

            await InvokeHandlersAsync(handler => handler.ReceivedMessageAsync(updateContext));

            return response;
        }

        /// <summary>
        /// Streams responses with the captured handler dispatch.
        /// </summary>
        /// <param name="deployment">The deployment.</param>
        /// <param name="messages">The messages.</param>
        /// <param name="context">The completion context.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The completion update stream.</returns>
        public async IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingAsync(
            AIDeployment deployment,
            IEnumerable<ChatMessage> messages,
            AICompletionContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(deployment);
            ArgumentNullException.ThrowIfNull(messages);
            ArgumentNullException.ThrowIfNull(context);

            var client = ResolveClient(deployment);

            await foreach (var chunk in client.CompleteStreamingAsync(messages, context, cancellationToken))
            {
                var updateContext = new ReceivedUpdateContext(chunk);

                await InvokeHandlersAsync(handler => handler.ReceivedUpdateAsync(updateContext));

                yield return chunk;
            }
        }

        /// <summary>
        /// Invokes handlers with the captured delegate-based dispatch.
        /// </summary>
        /// <param name="invoke">The handler delegate.</param>
        /// <returns>A task representing handler dispatch.</returns>
        private async Task InvokeHandlersAsync(Func<IAICompletionHandler, Task> invoke)
        {
            foreach (var handler in _completionHandlers)
            {
                try
                {
                    await invoke(handler);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error invoking completion handler '{HandlerType}'.", handler.GetType().Name);
                }
            }
        }

        /// <summary>
        /// Resolves the configured in-memory client.
        /// </summary>
        /// <param name="deployment">The deployment.</param>
        /// <returns>The completion client.</returns>
        private IAICompletionClient ResolveClient(AIDeployment deployment)
        {
            var clientName = deployment.ClientName
                ?? throw new AIDeploymentConfigurationException($"The deployment '{deployment.Name}' does not have a client name assigned.");

            if (!_aiOptions.Clients.TryGetValue(clientName, out var clientType))
            {
                throw new UnregisteredCompletionClientException(clientName);
            }

            return _serviceProvider.GetService(clientType) as IAICompletionClient
                ?? throw new InvalidOperationException($"No completion client registered for '{clientName}'.");
        }
    }

    /// <summary>
    /// Emits pre-created responses and updates without network or provider work.
    /// </summary>
    private sealed class InMemoryCompletionClient : IAICompletionClient
    {
        private readonly ChatResponseUpdate[] _updates;

        /// <summary>
        /// Initializes a new in-memory completion client.
        /// </summary>
        /// <param name="updates">The updates to emit.</param>
        public InMemoryCompletionClient(ChatResponseUpdate[] updates)
        {
            _updates = updates;
            Response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
        }

        /// <inheritdoc />
        public string ClientName => "benchmark-client";

        /// <summary>
        /// Gets the stable non-streaming response.
        /// </summary>
        public ChatResponse Response { get; }

        /// <inheritdoc />
        public Task<ChatResponse> CompleteAsync(
            IEnumerable<ChatMessage> messages,
            AICompletionContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Response);
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AICompletionContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var index = 0; index < _updates.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                yield return _updates[index];
            }

            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Observes handler inputs and optionally returns a cached faulted task.
    /// </summary>
    private sealed class RecordingCompletionHandler : AICompletionHandlerBase
    {
        private readonly Task _result;

        /// <summary>
        /// Initializes a new recording handler.
        /// </summary>
        /// <param name="handlerIndex">The handler index.</param>
        /// <param name="outcome">The handler outcome.</param>
        public RecordingCompletionHandler(
            int handlerIndex,
            CompletionHandlerOutcome outcome)
        {
            HandlerIndex = handlerIndex;
            Outcome = outcome;
            _result = outcome == CompletionHandlerOutcome.Faulting
                ? Task.FromException(new InvalidOperationException("benchmark handler failure"))
                : Task.CompletedTask;
        }

        /// <summary>
        /// Gets or sets whether detailed setup-only observations are captured.
        /// </summary>
        public bool Capture { get; set; }

        /// <summary>
        /// Gets the handler index.
        /// </summary>
        public int HandlerIndex { get; }

        /// <summary>
        /// Gets the configured outcome.
        /// </summary>
        public CompletionHandlerOutcome Outcome { get; }

        /// <summary>
        /// Gets the setup-only observations.
        /// </summary>
        public List<HandlerObservation> Observations { get; } = [];

        /// <inheritdoc />
        public override Task ReceivedMessageAsync(
            ReceivedMessageContext context,
            CancellationToken cancellationToken = default)
        {
            if (Capture)
            {
                Observations.Add(new HandlerObservation(
                    HandlerIndex,
                    context,
                    context.Completion,
                    cancellationToken));
            }

            return _result;
        }

        /// <inheritdoc />
        public override Task ReceivedUpdateAsync(
            ReceivedUpdateContext context,
            CancellationToken cancellationToken = default)
        {
            if (Capture)
            {
                Observations.Add(new HandlerObservation(
                    HandlerIndex,
                    context,
                    context.Update,
                    cancellationToken));
            }

            return _result;
        }

        /// <summary>
        /// Clears setup observations and disables capture.
        /// </summary>
        public void Reset()
        {
            Capture = false;
            Observations.Clear();
        }
    }

    /// <summary>
    /// Represents one setup-only handler observation.
    /// </summary>
    /// <param name="HandlerIndex">The handler index.</param>
    /// <param name="Context">The context reference.</param>
    /// <param name="Payload">The update or response reference.</param>
    /// <param name="CancellationToken">The handler cancellation token.</param>
    private sealed record HandlerObservation(
        int HandlerIndex,
        object Context,
        object Payload,
        CancellationToken CancellationToken);

    /// <summary>
    /// Counts logging calls without retaining state or formatting messages.
    /// </summary>
    private sealed class CountingLogger : ILogger<DefaultAICompletionService>
    {
        /// <summary>
        /// Gets the number of logging calls.
        /// </summary>
        public int LogCount { get; private set; }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NoOpScope.Instance;
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
        {
            return false;
        }

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            LogCount++;
        }

        /// <summary>
        /// Resets the logging count.
        /// </summary>
        public void Reset()
        {
            LogCount = 0;
        }
    }

    /// <summary>
    /// Provides a reusable no-op logging scope.
    /// </summary>
    private sealed class NoOpScope : IDisposable
    {
        /// <summary>
        /// Gets the singleton scope.
        /// </summary>
        public static NoOpScope Instance { get; } = new();

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
