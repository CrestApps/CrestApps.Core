using System.Collections;
using System.Runtime.CompilerServices;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Verifies the observable completion-handler dispatch behavior.
/// </summary>
public sealed class DefaultAICompletionServiceTests
{
    private const string ClientName = "test-client";
    private static readonly AIDeployment _deployment = new()
    {
        Name = "test-deployment",
        ClientName = ClientName,
    };
    private static readonly ChatMessage[] _messages =
    [
        new(ChatRole.User, "hello"),
    ];

    /// <summary>
    /// Verifies zero, one, and multiple handlers are invoked once per update.
    /// </summary>
    /// <param name="handlerCount">The number of handlers.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task CompleteStreamingAsync_WithHandlers_InvokesEachOncePerUpdate(int handlerCount)
    {
        var updates = CreateUpdates(2);
        var invocationCounts = new int[handlerCount];
        var handlers = new IAICompletionHandler[handlerCount];

        for (var index = 0; index < handlers.Length; index++)
        {
            var handlerIndex = index;
            handlers[index] = new DelegateCompletionHandler(
                onUpdate: (_, _) =>
                {
                    invocationCounts[handlerIndex]++;

                    return Task.CompletedTask;
                });
        }

        var client = new TestCompletionClient(
            streamFactory: cancellationToken => StreamUpdatesAsync(updates, cancellationToken));
        var service = CreateService(client, handlers);

        var result = await CollectAsync(service.CompleteStreamingAsync(
            _deployment,
            _messages,
            new AICompletionContext(),
            TestContext.Current.CancellationToken));

        Assert.Equal(updates.Length, result.Count);

        for (var index = 0; index < updates.Length; index++)
        {
            Assert.Same(updates[index], result[index]);
        }

        Assert.All(invocationCounts, count => Assert.Equal(updates.Length, count));
    }

    /// <summary>
    /// Verifies handlers run sequentially before each update is exposed and share one context per update.
    /// </summary>
    [Fact]
    public async Task CompleteStreamingAsync_WithMultipleHandlers_PreservesOrderTimingAndIdentity()
    {
        var updates = CreateUpdates(2);
        var events = new List<string>();
        var observations = new List<(int HandlerIndex, ReceivedUpdateContext Context, CancellationToken Token)>();
        var handlers = Enumerable.Range(0, 3)
            .Select(handlerIndex => (IAICompletionHandler)new DelegateCompletionHandler(
                onUpdate: (context, cancellationToken) =>
                {
                    var updateIndex = Array.IndexOf(updates, context.Update);
                    events.Add($"handler-{handlerIndex}-update-{updateIndex}");
                    observations.Add((handlerIndex, context, cancellationToken));

                    return Task.CompletedTask;
                }))
            .ToArray();
        var client = new TestCompletionClient(
            streamFactory: cancellationToken => StreamUpdatesAsync(updates, cancellationToken));
        var service = CreateService(client, handlers);
        var result = new List<ChatResponseUpdate>();

        await foreach (var update in service.CompleteStreamingAsync(
            _deployment,
            _messages,
            new AICompletionContext(),
            TestContext.Current.CancellationToken))
        {
            events.Add($"consumer-update-{result.Count}");
            result.Add(update);
        }

        Assert.Equal(
        [
            "handler-0-update-0",
            "handler-1-update-0",
            "handler-2-update-0",
            "consumer-update-0",
            "handler-0-update-1",
            "handler-1-update-1",
            "handler-2-update-1",
            "consumer-update-1",
        ],
            events);
        Assert.Equal(6, observations.Count);
        Assert.Same(observations[0].Context, observations[1].Context);
        Assert.Same(observations[0].Context, observations[2].Context);
        Assert.Same(observations[3].Context, observations[4].Context);
        Assert.Same(observations[3].Context, observations[5].Context);
        Assert.NotSame(observations[0].Context, observations[3].Context);
        Assert.Same(updates[0], observations[0].Context.Update);
        Assert.Same(updates[1], observations[3].Context.Update);
        Assert.All(observations, observation => Assert.False(observation.Token.CanBeCanceled));
        Assert.Same(updates[0], result[0]);
        Assert.Same(updates[1], result[1]);
    }

    /// <summary>
    /// Verifies a consumer cannot observe an update until the current handler has completed.
    /// </summary>
    [Fact]
    public async Task CompleteStreamingAsync_WhenHandlerBlocks_DelaysYieldUntilHandlerCompletes()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, "one");
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateCompletionHandler(
            onUpdate: async (_, _) =>
            {
                handlerStarted.SetResult();
                await releaseHandler.Task;
            });
        var client = new TestCompletionClient(
            streamFactory: cancellationToken => StreamUpdatesAsync([update], cancellationToken));
        var service = CreateService(client, [handler]);
        await using var enumerator = service.CompleteStreamingAsync(
            _deployment,
            _messages,
            new AICompletionContext(),
            TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        var moveNextTask = enumerator.MoveNextAsync().AsTask();
        await handlerStarted.Task;

        Assert.False(moveNextTask.IsCompleted);

        releaseHandler.SetResult();

        Assert.True(await moveNextTask);
        Assert.Same(update, enumerator.Current);
    }

    /// <summary>
    /// Verifies one or multiple handler failures are logged and do not stop later handlers or updates.
    /// </summary>
    /// <param name="failureCount">The number of failing handlers before the healthy handler.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CompleteStreamingAsync_WhenHandlersThrow_LogsAndContinues(
        int failureCount)
    {
        var updates = CreateUpdates(2);
        var exceptions = Enumerable.Range(0, failureCount)
            .Select(index => (Exception)new InvalidOperationException($"failure-{index}"))
            .ToArray();
        var handlers = new List<IAICompletionHandler>();

        for (var index = 0; index < failureCount; index++)
        {
            var exception = exceptions[index];
            handlers.Add(index % 2 == 0
                ? new SynchronouslyThrowingCompletionHandler(exception)
                : new FaultedTaskCompletionHandler(exception));
        }

        var healthyInvocationCount = 0;
        handlers.Add(new DelegateCompletionHandler(
            onUpdate: (_, _) =>
            {
                healthyInvocationCount++;

                return Task.CompletedTask;
            }));

        var logger = new RecordingLogger<DefaultAICompletionService>();
        var client = new TestCompletionClient(
            streamFactory: cancellationToken => StreamUpdatesAsync(updates, cancellationToken));
        var service = CreateService(client, handlers, logger);

        var result = await CollectAsync(service.CompleteStreamingAsync(
            _deployment,
            _messages,
            new AICompletionContext(),
            TestContext.Current.CancellationToken));

        Assert.Equal(updates, result);
        Assert.Equal(updates.Length, healthyInvocationCount);
        Assert.Equal(failureCount * updates.Length, logger.Entries.Count);

        for (var updateIndex = 0; updateIndex < updates.Length; updateIndex++)
        {
            for (var handlerIndex = 0; handlerIndex < failureCount; handlerIndex++)
            {
                var entry = logger.Entries[(updateIndex * failureCount) + handlerIndex];
                var expectedHandlerType = handlerIndex % 2 == 0
                    ? nameof(SynchronouslyThrowingCompletionHandler)
                    : nameof(FaultedTaskCompletionHandler);

                Assert.Equal(LogLevel.Error, entry.Level);
                Assert.Same(exceptions[handlerIndex], entry.Exception);
                Assert.Equal(
                    $"Error invoking completion handler '{expectedHandlerType}'.",
                    entry.Message);
                Assert.Equal(
                    "Error invoking completion handler '{HandlerType}'.",
                    entry.OriginalFormat);
                Assert.Equal(expectedHandlerType, entry.HandlerType);
            }
        }
    }

    /// <summary>
    /// Verifies a cancellation exception raised by a handler is logged and swallowed like any other handler exception.
    /// </summary>
    [Fact]
    public async Task CompleteStreamingAsync_WhenHandlerThrowsCancellation_LogsAndContinues()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, "one");
        var exception = new OperationCanceledException("handler-cancelled");
        var laterHandlerCount = 0;
        IAICompletionHandler[] handlers =
        [
            new SynchronouslyThrowingCompletionHandler(exception),
            new DelegateCompletionHandler(
                onUpdate: (_, _) =>
                {
                    laterHandlerCount++;

                    return Task.CompletedTask;
                }),
        ];
        var logger = new RecordingLogger<DefaultAICompletionService>();
        var client = new TestCompletionClient(
            streamFactory: cancellationToken => StreamUpdatesAsync([update], cancellationToken));
        var service = CreateService(client, handlers, logger);

        var result = await CollectAsync(service.CompleteStreamingAsync(
            _deployment,
            _messages,
            new AICompletionContext(),
            TestContext.Current.CancellationToken));

        Assert.Single(result);
        Assert.Equal(1, laterHandlerCount);
        var entry = Assert.Single(logger.Entries);
        Assert.Same(exception, entry.Exception);
    }

    /// <summary>
    /// Verifies the handler enumerable remains lazy, is enumerated for every update, and observes mutations between updates.
    /// </summary>
    [Fact]
    public async Task CompleteStreamingAsync_WithMutableHandlerEnumerable_ReenumeratesForEveryUpdate()
    {
        var updates = CreateUpdates(2);
        var firstHandlerCount = 0;
        var secondHandlerCount = 0;
        var handlers = new List<IAICompletionHandler>
        {
            new DelegateCompletionHandler(
                onUpdate: (_, _) =>
                {
                    firstHandlerCount++;

                    return Task.CompletedTask;
                }),
        };
        var trackingHandlers = new TrackingHandlerEnumerable(handlers);
        var client = new TestCompletionClient(
            streamFactory: cancellationToken => StreamUpdatesAsync(updates, cancellationToken));
        var service = CreateService(client, trackingHandlers);
        var stream = service.CompleteStreamingAsync(
            _deployment,
            _messages,
            new AICompletionContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, client.StreamingCallCount);
        Assert.Equal(0, trackingHandlers.GetEnumeratorCount);

        await using var enumerator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.Equal(0, client.StreamingCallCount);
        Assert.Equal(0, trackingHandlers.GetEnumeratorCount);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(1, client.StreamingCallCount);
        Assert.Equal(1, trackingHandlers.GetEnumeratorCount);
        Assert.Equal(1, firstHandlerCount);
        Assert.Equal(0, secondHandlerCount);

        handlers.Add(new DelegateCompletionHandler(
            onUpdate: (_, _) =>
            {
                secondHandlerCount++;

                return Task.CompletedTask;
            }));

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(2, trackingHandlers.GetEnumeratorCount);
        Assert.Equal(2, firstHandlerCount);
        Assert.Equal(1, secondHandlerCount);
        Assert.False(await enumerator.MoveNextAsync());
    }

    /// <summary>
    /// Verifies a single-use handler enumerable succeeds for one update and fails on the second enumeration.
    /// </summary>
    [Fact]
    public async Task CompleteStreamingAsync_WithSingleUseHandlerEnumerable_SecondUpdateFailsAndDisposesSource()
    {
        var updates = CreateUpdates(2);
        var sourceDisposed = false;
        var expectedException = new InvalidOperationException("second handler enumeration");
        var handlers = new SingleUseHandlerEnumerable(
            [new DelegateCompletionHandler()],
            expectedException);
        var client = new TestCompletionClient(
            streamFactory: cancellationToken => StreamUpdatesAsync(
                updates,
                cancellationToken,
                onDisposed: () => sourceDisposed = true));
        var service = CreateService(client, handlers);
        await using var enumerator = service.CompleteStreamingAsync(
            _deployment,
            _messages,
            new AICompletionContext(),
            TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Same(updates[0], enumerator.Current);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await enumerator.MoveNextAsync());

        Assert.Same(expectedException, exception);
        Assert.Equal(2, handlers.GetEnumeratorCount);
        Assert.True(sourceDisposed);
    }

    /// <summary>
    /// Verifies a null source update retains the context constructor's exact failure even with no handlers.
    /// </summary>
    [Fact]
    public async Task CompleteStreamingAsync_WithNullUpdateAndZeroHandlers_ThrowsExactArgumentException()
    {
        ChatResponseUpdate[] updates = [null];
        var sourceDisposed = false;
        var client = new TestCompletionClient(
            streamFactory: cancellationToken => StreamUpdatesAsync(
                updates,
                cancellationToken,
                onDisposed: () => sourceDisposed = true));
        var service = CreateService(client, []);

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await CollectAsync(service.CompleteStreamingAsync(
                _deployment,
                _messages,
                new AICompletionContext(),
                TestContext.Current.CancellationToken)));

        Assert.Equal("update", exception.ParamName);
        Assert.True(sourceDisposed);
    }

    /// <summary>
    /// Verifies handler-enumerator failures are not swallowed or logged and dispose the source stream.
    /// </summary>
    [Fact]
    public async Task CompleteStreamingAsync_WhenHandlerEnumerationThrows_PropagatesWithoutLogging()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, "one");
        var sourceDisposed = false;
        var expectedException = new InvalidOperationException("handler move-next");
        var handlers = new MoveNextThrowingHandlerEnumerable(
            new DelegateCompletionHandler(),
            expectedException);
        var logger = new RecordingLogger<DefaultAICompletionService>();
        var client = new TestCompletionClient(
            streamFactory: cancellationToken => StreamUpdatesAsync(
                [update],
                cancellationToken,
                onDisposed: () => sourceDisposed = true));
        var service = CreateService(client, handlers, logger);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await CollectAsync(service.CompleteStreamingAsync(
                _deployment,
                _messages,
                new AICompletionContext(),
                TestContext.Current.CancellationToken)));

        Assert.Same(expectedException, exception);
        Assert.Empty(logger.Entries);
        Assert.True(sourceDisposed);
    }

    /// <summary>
    /// Verifies an empty source does not enumerate handlers and still disposes its source enumerator.
    /// </summary>
    [Fact]
    public async Task CompleteStreamingAsync_WithEmptySource_DoesNotEnumerateHandlers()
    {
        var sourceDisposed = false;
        var expectedException = new InvalidOperationException("handlers should not be enumerated");
        var handlers = new GetEnumeratorThrowingHandlerEnumerable(expectedException);
        var client = new TestCompletionClient(
            streamFactory: cancellationToken => StreamUpdatesAsync(
                [],
                cancellationToken,
                onDisposed: () => sourceDisposed = true));
        var service = CreateService(client, handlers);

        var result = await CollectAsync(service.CompleteStreamingAsync(
            _deployment,
            _messages,
            new AICompletionContext(),
            TestContext.Current.CancellationToken));

        Assert.Empty(result);
        Assert.Equal(0, handlers.GetEnumeratorCount);
        Assert.True(sourceDisposed);
    }

    /// <summary>
    /// Verifies a source failure propagates after prior updates and disposes the source enumerator.
    /// </summary>
    [Fact]
    public async Task CompleteStreamingAsync_WhenSourceThrows_PropagatesExactExceptionAndDisposes()
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, "one");
        var expectedException = new InvalidOperationException("source failure");
        var sourceDisposed = false;
        var handlerCount = 0;
        var handler = new DelegateCompletionHandler(
            onUpdate: (_, _) =>
            {
                handlerCount++;

                return Task.CompletedTask;
            });
        var client = new TestCompletionClient(
            streamFactory: cancellationToken => StreamUpdatesAsync(
                [update],
                cancellationToken,
                exceptionAfterUpdates: expectedException,
                onDisposed: () => sourceDisposed = true));
        var service = CreateService(client, [handler]);
        await using var enumerator = service.CompleteStreamingAsync(
            _deployment,
            _messages,
            new AICompletionContext(),
            TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Same(update, enumerator.Current);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await enumerator.MoveNextAsync());

        Assert.Same(expectedException, exception);
        Assert.Equal(1, handlerCount);
        Assert.True(sourceDisposed);
    }

    /// <summary>
    /// Verifies source cancellation propagates, reaches the client, and disposes the source enumerator.
    /// </summary>
    [Fact]
    public async Task CompleteStreamingAsync_WhenSourceIsCancelled_PropagatesAndDisposes()
    {
        using var cancellationSource = new CancellationTokenSource();
        var updates = CreateUpdates(2);
        var sourceDisposed = false;
        var handlerCount = 0;
        var handler = new DelegateCompletionHandler(
            onUpdate: (_, handlerToken) =>
            {
                Assert.False(handlerToken.CanBeCanceled);
                handlerCount++;

                return Task.CompletedTask;
            });
        var client = new TestCompletionClient(
            streamFactory: cancellationToken => StreamUpdatesAsync(
                updates,
                cancellationToken,
                onDisposed: () => sourceDisposed = true));
        var service = CreateService(client, [handler]);
        await using var enumerator = service.CompleteStreamingAsync(
            _deployment,
            _messages,
            new AICompletionContext(),
            cancellationSource.Token).GetAsyncEnumerator(cancellationSource.Token);

        Assert.True(await enumerator.MoveNextAsync());
        cancellationSource.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await enumerator.MoveNextAsync());

        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
        Assert.Equal(cancellationSource.Token, client.LastStreamingCancellationToken);
        Assert.Equal(1, handlerCount);
        Assert.True(sourceDisposed);
    }

    /// <summary>
    /// Verifies an enumerator cancellation token flows through the service to the source client.
    /// </summary>
    [Fact]
    public async Task CompleteStreamingAsync_WithEnumeratorCancellation_PassesTokenToClient()
    {
        using var cancellationSource = new CancellationTokenSource();
        var update = new ChatResponseUpdate(ChatRole.Assistant, "one");
        var client = new TestCompletionClient(
            streamFactory: cancellationToken => StreamUpdatesAsync([update], cancellationToken));
        var service = CreateService(client, []);

#pragma warning disable xUnit1051
        var result = await CollectAsync(service.CompleteStreamingAsync(
            _deployment,
            _messages,
            new AICompletionContext()).WithCancellation(cancellationSource.Token));
#pragma warning restore xUnit1051

        Assert.Single(result);
        Assert.Equal(cancellationSource.Token, client.LastStreamingCancellationToken);
    }

    /// <summary>
    /// Verifies distinct method and enumerator tokens are linked before reaching the source client.
    /// </summary>
    [Fact]
    public async Task CompleteStreamingAsync_WithDistinctCancellationTokens_PassesLinkedToken()
    {
        using var methodCancellationSource = new CancellationTokenSource();
        using var enumeratorCancellationSource = new CancellationTokenSource();
        var updates = CreateUpdates(2);
        var sourceDisposed = false;
        var client = new TestCompletionClient(
            streamFactory: cancellationToken => StreamUpdatesAsync(
                updates,
                cancellationToken,
                onDisposed: () => sourceDisposed = true));
        var service = CreateService(client, []);
        await using var enumerator = service.CompleteStreamingAsync(
            _deployment,
            _messages,
            new AICompletionContext(),
            methodCancellationSource.Token).GetAsyncEnumerator(enumeratorCancellationSource.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.NotEqual(methodCancellationSource.Token, client.LastStreamingCancellationToken);
        Assert.NotEqual(enumeratorCancellationSource.Token, client.LastStreamingCancellationToken);

        enumeratorCancellationSource.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await enumerator.MoveNextAsync());

        Assert.Equal(client.LastStreamingCancellationToken, exception.CancellationToken);
        Assert.True(client.LastStreamingCancellationToken.IsCancellationRequested);
        Assert.True(sourceDisposed);
    }

    /// <summary>
    /// Verifies disposing the consumer enumerator disposes the source without processing later updates.
    /// </summary>
    [Fact]
    public async Task CompleteStreamingAsync_WhenConsumerStops_DisposesSourceImmediately()
    {
        var updates = CreateUpdates(3);
        var sourceDisposed = false;
        var handlerCount = 0;
        var handler = new DelegateCompletionHandler(
            onUpdate: (_, _) =>
            {
                handlerCount++;

                return Task.CompletedTask;
            });
        var client = new TestCompletionClient(
            streamFactory: cancellationToken => StreamUpdatesAsync(
                updates,
                cancellationToken,
                onDisposed: () => sourceDisposed = true));
        var service = CreateService(client, [handler]);
        var enumerator = service.CompleteStreamingAsync(
            _deployment,
            _messages,
            new AICompletionContext(),
            TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Same(updates[0], enumerator.Current);

        await enumerator.DisposeAsync();

        Assert.Equal(1, handlerCount);
        Assert.True(sourceDisposed);
    }

    /// <summary>
    /// Verifies zero, one, and multiple message handlers share one context and return the original response.
    /// </summary>
    /// <param name="handlerCount">The number of handlers.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task CompleteAsync_WithHandlers_PreservesOrderContextAndResponseIdentity(
        int handlerCount)
    {
        using var cancellationSource = new CancellationTokenSource();
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
        var observations = new List<(int HandlerIndex, ReceivedMessageContext Context, CancellationToken Token)>();
        var handlers = Enumerable.Range(0, handlerCount)
            .Select(handlerIndex => (IAICompletionHandler)new DelegateCompletionHandler(
                onMessage: (context, cancellationToken) =>
                {
                    observations.Add((handlerIndex, context, cancellationToken));

                    return Task.CompletedTask;
                }))
            .ToArray();
        var client = new TestCompletionClient(response: response);
        var service = CreateService(client, handlers);

        var result = await service.CompleteAsync(
            _deployment,
            _messages,
            new AICompletionContext(),
            cancellationSource.Token);

        Assert.Same(response, result);
        Assert.Equal(cancellationSource.Token, client.LastCompletionCancellationToken);
        Assert.Equal(handlerCount, observations.Count);

        for (var index = 0; index < observations.Count; index++)
        {
            Assert.Equal(index, observations[index].HandlerIndex);
            Assert.Same(response, observations[index].Context.Completion);
            Assert.False(observations[index].Token.CanBeCanceled);

            if (index > 0)
            {
                Assert.Same(observations[0].Context, observations[index].Context);
            }
        }
    }

    /// <summary>
    /// Verifies non-streaming handler failures are logged and later handlers still run before the response returns.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_WhenHandlersThrow_LogsAndContinues()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
        var firstException = new InvalidOperationException("first");
        var secondException = new InvalidOperationException("second");
        var laterHandlerCount = 0;
        IAICompletionHandler[] handlers =
        [
            new SynchronouslyThrowingCompletionHandler(firstException),
            new FaultedTaskCompletionHandler(secondException),
            new DelegateCompletionHandler(
                onMessage: (_, _) =>
                {
                    laterHandlerCount++;

                    return Task.CompletedTask;
                }),
        ];
        var logger = new RecordingLogger<DefaultAICompletionService>();
        var client = new TestCompletionClient(response: response);
        var service = CreateService(client, handlers, logger);

        var result = await service.CompleteAsync(
            _deployment,
            _messages,
            new AICompletionContext(),
            TestContext.Current.CancellationToken);

        Assert.Same(response, result);
        Assert.Equal(1, laterHandlerCount);
        Assert.Equal(2, logger.Entries.Count);
        Assert.Same(firstException, logger.Entries[0].Exception);
        Assert.Same(secondException, logger.Entries[1].Exception);
    }

    /// <summary>
    /// Verifies non-streaming handlers remain unenumerated until the client response completes.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_EnumeratesHandlersOnlyAfterClientResponseCompletes()
    {
        var responseSource = new TaskCompletionSource<ChatResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handlers = new TrackingHandlerEnumerable([new DelegateCompletionHandler()]);
        var client = new TestCompletionClient(
            completionFactory: _ => responseSource.Task);
        var service = CreateService(client, handlers);

        var completionTask = service.CompleteAsync(
            _deployment,
            _messages,
            new AICompletionContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, client.CompletionCallCount);
        Assert.Equal(0, handlers.GetEnumeratorCount);

        responseSource.SetResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        await completionTask;

        Assert.Equal(1, handlers.GetEnumeratorCount);
    }

    /// <summary>
    /// Verifies a completion-client failure propagates without enumerating completion handlers.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_WhenClientThrows_PropagatesWithoutEnumeratingHandlers()
    {
        var expectedException = new InvalidOperationException("client failure");
        var handlers = new GetEnumeratorThrowingHandlerEnumerable(
            new InvalidOperationException("handlers should not be enumerated"));
        var client = new TestCompletionClient(
            completionFactory: _ => Task.FromException<ChatResponse>(expectedException));
        var service = CreateService(client, handlers);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompleteAsync(
                _deployment,
                _messages,
                new AICompletionContext(),
                TestContext.Current.CancellationToken));

        Assert.Same(expectedException, exception);
        Assert.Equal(0, handlers.GetEnumeratorCount);
    }

    /// <summary>
    /// Verifies a null non-streaming client result retains the exact service error and skips handlers.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_WhenClientReturnsNull_ThrowsExactErrorWithoutEnumeratingHandlers()
    {
        var handlers = new GetEnumeratorThrowingHandlerEnumerable(
            new InvalidOperationException("handlers should not be enumerated"));
        var client = new TestCompletionClient(
            completionFactory: _ => Task.FromResult<ChatResponse>(null));
        var service = CreateService(client, handlers);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompleteAsync(
                _deployment,
                _messages,
                new AICompletionContext(),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            "Unable to generate a response. Ensure that the connection, and the deployment names are correct.",
            exception.Message);
        Assert.Equal(0, handlers.GetEnumeratorCount);
    }

    /// <summary>
    /// Verifies the default registrations provide handlers to consumers as an ordered array.
    /// </summary>
    [Fact]
    public void AddCoreAIServices_ResolvesCompletionHandlersAsOrderedArray()
    {
        var first = new DelegateCompletionHandler();
        var second = new DelegateCompletionHandler();
        var services = new ServiceCollection();
        services.AddCoreAIServices();
        services.AddSingleton<IAICompletionHandler>(first);
        services.AddSingleton<IAICompletionHandler>(second);
        using var serviceProvider = services.BuildServiceProvider();

        var handlers = serviceProvider.GetRequiredService<IEnumerable<IAICompletionHandler>>();
        var handlerArray = Assert.IsType<IAICompletionHandler[]>(handlers);

        Assert.Equal(2, handlerArray.Length);
        Assert.Same(first, handlerArray[0]);
        Assert.Same(second, handlerArray[1]);
    }

    /// <summary>
    /// Creates the service with a single in-memory completion client.
    /// </summary>
    /// <param name="client">The completion client.</param>
    /// <param name="handlers">The completion handlers.</param>
    /// <param name="logger">The optional recording logger.</param>
    /// <returns>The configured completion service.</returns>
    private static DefaultAICompletionService CreateService(
        TestCompletionClient client,
        IEnumerable<IAICompletionHandler> handlers,
        ILogger<DefaultAICompletionService> logger = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(client);
        var serviceProvider = services.BuildServiceProvider();
        var options = new AIOptions();
        options.AddClient<TestCompletionClient>(ClientName);

        return new DefaultAICompletionService(
            serviceProvider,
            handlers,
            Options.Create(options),
            logger ?? new RecordingLogger<DefaultAICompletionService>());
    }

    /// <summary>
    /// Creates streaming updates with stable identities.
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
    /// Collects a streaming result while preserving each yielded update reference.
    /// </summary>
    /// <param name="updates">The source updates.</param>
    /// <returns>The collected updates.</returns>
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
    /// Collects a cancellable streaming result while preserving each yielded update reference.
    /// </summary>
    /// <param name="updates">The source updates.</param>
    /// <returns>The collected updates.</returns>
    private static async Task<List<ChatResponseUpdate>> CollectAsync(
        ConfiguredCancelableAsyncEnumerable<ChatResponseUpdate> updates)
    {
        var result = new List<ChatResponseUpdate>();

        await foreach (var update in updates)
        {
            result.Add(update);
        }

        return result;
    }

    /// <summary>
    /// Streams the supplied updates and records deterministic disposal.
    /// </summary>
    /// <param name="updates">The updates.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="exceptionAfterUpdates">The optional exception raised after all updates.</param>
    /// <param name="onDisposed">The optional disposal callback.</param>
    /// <returns>The asynchronous update stream.</returns>
    private static async IAsyncEnumerable<ChatResponseUpdate> StreamUpdatesAsync(
        ChatResponseUpdate[] updates,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        Exception exceptionAfterUpdates = null,
        Action onDisposed = null)
    {
        try
        {
            for (var index = 0; index < updates.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                yield return updates[index];
            }

            if (exceptionAfterUpdates is not null)
            {
                throw exceptionAfterUpdates;
            }

            await Task.CompletedTask;
        }
        finally
        {
            onDisposed?.Invoke();
        }
    }

    /// <summary>
    /// Provides configurable in-memory completion responses.
    /// </summary>
    private sealed class TestCompletionClient : IAICompletionClient
    {
        private readonly Func<CancellationToken, Task<ChatResponse>> _completionFactory;
        private readonly Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> _streamFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestCompletionClient"/> class.
        /// </summary>
        /// <param name="response">The non-streaming response.</param>
        /// <param name="completionFactory">The optional non-streaming factory.</param>
        /// <param name="streamFactory">The optional streaming factory.</param>
        public TestCompletionClient(
            ChatResponse response = null,
            Func<CancellationToken, Task<ChatResponse>> completionFactory = null,
            Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> streamFactory = null)
        {
            _completionFactory = completionFactory
                ?? (_ => Task.FromResult(response
                    ?? new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"))));
            _streamFactory = streamFactory
                ?? (cancellationToken => StreamUpdatesAsync([], cancellationToken));
        }

        /// <inheritdoc />
        public string ClientName => DefaultAICompletionServiceTests.ClientName;

        /// <summary>
        /// Gets the number of non-streaming calls.
        /// </summary>
        public int CompletionCallCount { get; private set; }

        /// <summary>
        /// Gets the cancellation token supplied to the last non-streaming call.
        /// </summary>
        public CancellationToken LastCompletionCancellationToken { get; private set; }

        /// <summary>
        /// Gets the cancellation token supplied to the last streaming call.
        /// </summary>
        public CancellationToken LastStreamingCancellationToken { get; private set; }

        /// <summary>
        /// Gets the number of streaming calls.
        /// </summary>
        public int StreamingCallCount { get; private set; }

        /// <inheritdoc />
        public Task<ChatResponse> CompleteAsync(
            IEnumerable<ChatMessage> messages,
            AICompletionContext context,
            CancellationToken cancellationToken = default)
        {
            CompletionCallCount++;
            LastCompletionCancellationToken = cancellationToken;

            return _completionFactory(cancellationToken);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AICompletionContext context,
            CancellationToken cancellationToken = default)
        {
            StreamingCallCount++;
            LastStreamingCancellationToken = cancellationToken;

            return _streamFactory(cancellationToken);
        }
    }

    /// <summary>
    /// Provides delegate-backed completion-handler behavior.
    /// </summary>
    private sealed class DelegateCompletionHandler : AICompletionHandlerBase
    {
        private readonly Func<ReceivedMessageContext, CancellationToken, Task> _onMessage;
        private readonly Func<ReceivedUpdateContext, CancellationToken, Task> _onUpdate;

        /// <summary>
        /// Initializes a new instance of the <see cref="DelegateCompletionHandler"/> class.
        /// </summary>
        /// <param name="onMessage">The optional message callback.</param>
        /// <param name="onUpdate">The optional update callback.</param>
        public DelegateCompletionHandler(
            Func<ReceivedMessageContext, CancellationToken, Task> onMessage = null,
            Func<ReceivedUpdateContext, CancellationToken, Task> onUpdate = null)
        {
            _onMessage = onMessage;
            _onUpdate = onUpdate;
        }

        /// <inheritdoc />
        public override Task ReceivedMessageAsync(
            ReceivedMessageContext context,
            CancellationToken cancellationToken = default)
        {
            return _onMessage?.Invoke(context, cancellationToken) ?? Task.CompletedTask;
        }

        /// <inheritdoc />
        public override Task ReceivedUpdateAsync(
            ReceivedUpdateContext context,
            CancellationToken cancellationToken = default)
        {
            return _onUpdate?.Invoke(context, cancellationToken) ?? Task.CompletedTask;
        }
    }

    /// <summary>
    /// Throws synchronously from both completion-handler methods.
    /// </summary>
    private sealed class SynchronouslyThrowingCompletionHandler : AICompletionHandlerBase
    {
        private readonly Exception _exception;

        /// <summary>
        /// Initializes a new instance of the <see cref="SynchronouslyThrowingCompletionHandler"/> class.
        /// </summary>
        /// <param name="exception">The exception to throw.</param>
        public SynchronouslyThrowingCompletionHandler(Exception exception)
        {
            _exception = exception;
        }

        /// <inheritdoc />
        public override Task ReceivedMessageAsync(
            ReceivedMessageContext context,
            CancellationToken cancellationToken = default)
        {
            throw _exception;
        }

        /// <inheritdoc />
        public override Task ReceivedUpdateAsync(
            ReceivedUpdateContext context,
            CancellationToken cancellationToken = default)
        {
            throw _exception;
        }
    }

    /// <summary>
    /// Returns a faulted task from both completion-handler methods.
    /// </summary>
    private sealed class FaultedTaskCompletionHandler : AICompletionHandlerBase
    {
        private readonly Task _faultedTask;

        /// <summary>
        /// Initializes a new instance of the <see cref="FaultedTaskCompletionHandler"/> class.
        /// </summary>
        /// <param name="exception">The task exception.</param>
        public FaultedTaskCompletionHandler(Exception exception)
        {
            _faultedTask = Task.FromException(exception);
        }

        /// <inheritdoc />
        public override Task ReceivedMessageAsync(
            ReceivedMessageContext context,
            CancellationToken cancellationToken = default)
        {
            return _faultedTask;
        }

        /// <inheritdoc />
        public override Task ReceivedUpdateAsync(
            ReceivedUpdateContext context,
            CancellationToken cancellationToken = default)
        {
            return _faultedTask;
        }
    }

    /// <summary>
    /// Tracks every request for a handler enumerator.
    /// </summary>
    private sealed class TrackingHandlerEnumerable : IEnumerable<IAICompletionHandler>
    {
        private readonly IEnumerable<IAICompletionHandler> _handlers;

        /// <summary>
        /// Initializes a new instance of the <see cref="TrackingHandlerEnumerable"/> class.
        /// </summary>
        /// <param name="handlers">The handlers.</param>
        public TrackingHandlerEnumerable(IEnumerable<IAICompletionHandler> handlers)
        {
            _handlers = handlers;
        }

        /// <summary>
        /// Gets the number of generic enumerators requested.
        /// </summary>
        public int GetEnumeratorCount { get; private set; }

        /// <inheritdoc />
        public IEnumerator<IAICompletionHandler> GetEnumerator()
        {
            GetEnumeratorCount++;

            return _handlers.GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /// <summary>
    /// Allows one handler enumeration and fails the next request.
    /// </summary>
    private sealed class SingleUseHandlerEnumerable : IEnumerable<IAICompletionHandler>
    {
        private readonly Exception _secondEnumerationException;
        private readonly IEnumerable<IAICompletionHandler> _handlers;

        /// <summary>
        /// Initializes a new instance of the <see cref="SingleUseHandlerEnumerable"/> class.
        /// </summary>
        /// <param name="handlers">The handlers.</param>
        /// <param name="secondEnumerationException">The second-enumeration exception.</param>
        public SingleUseHandlerEnumerable(
            IEnumerable<IAICompletionHandler> handlers,
            Exception secondEnumerationException)
        {
            _handlers = handlers;
            _secondEnumerationException = secondEnumerationException;
        }

        /// <summary>
        /// Gets the number of generic enumerators requested.
        /// </summary>
        public int GetEnumeratorCount { get; private set; }

        /// <inheritdoc />
        public IEnumerator<IAICompletionHandler> GetEnumerator()
        {
            GetEnumeratorCount++;

            if (GetEnumeratorCount > 1)
            {
                throw _secondEnumerationException;
            }

            return _handlers.GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /// <summary>
    /// Throws while advancing past its first handler.
    /// </summary>
    private sealed class MoveNextThrowingHandlerEnumerable : IEnumerable<IAICompletionHandler>
    {
        private readonly Exception _exception;
        private readonly IAICompletionHandler _handler;

        /// <summary>
        /// Initializes a new instance of the <see cref="MoveNextThrowingHandlerEnumerable"/> class.
        /// </summary>
        /// <param name="handler">The first handler.</param>
        /// <param name="exception">The exception raised on the second move.</param>
        public MoveNextThrowingHandlerEnumerable(
            IAICompletionHandler handler,
            Exception exception)
        {
            _handler = handler;
            _exception = exception;
        }

        /// <inheritdoc />
        public IEnumerator<IAICompletionHandler> GetEnumerator()
        {
            yield return _handler;
            throw _exception;
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /// <summary>
    /// Throws whenever a handler enumerator is requested.
    /// </summary>
    private sealed class GetEnumeratorThrowingHandlerEnumerable : IEnumerable<IAICompletionHandler>
    {
        private readonly Exception _exception;

        /// <summary>
        /// Initializes a new instance of the <see cref="GetEnumeratorThrowingHandlerEnumerable"/> class.
        /// </summary>
        /// <param name="exception">The exception to throw.</param>
        public GetEnumeratorThrowingHandlerEnumerable(Exception exception)
        {
            _exception = exception;
        }

        /// <summary>
        /// Gets the number of generic enumerators requested.
        /// </summary>
        public int GetEnumeratorCount { get; private set; }

        /// <inheritdoc />
        public IEnumerator<IAICompletionHandler> GetEnumerator()
        {
            GetEnumeratorCount++;

            throw _exception;
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /// <summary>
    /// Records structured log entries for exact compatibility assertions.
    /// </summary>
    /// <typeparam name="T">The logging category type.</typeparam>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        /// <summary>
        /// Gets the recorded log entries.
        /// </summary>
        public List<LogEntry> Entries { get; } = [];

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            string originalFormat = null;
            string handlerType = null;

            if (state is IEnumerable<KeyValuePair<string, object>> values)
            {
                foreach (var value in values)
                {
                    if (string.Equals(value.Key, "{OriginalFormat}", StringComparison.Ordinal))
                    {
                        originalFormat = value.Value?.ToString();
                    }
                    else if (string.Equals(value.Key, "HandlerType", StringComparison.Ordinal))
                    {
                        handlerType = value.Value?.ToString();
                    }
                }
            }

            Entries.Add(new LogEntry(
                logLevel,
                exception,
                formatter(state, exception),
                originalFormat,
                handlerType));
        }
    }

    /// <summary>
    /// Represents one captured structured log entry.
    /// </summary>
    /// <param name="Level">The log level.</param>
    /// <param name="Exception">The logged exception.</param>
    /// <param name="Message">The formatted message.</param>
    /// <param name="OriginalFormat">The original message template.</param>
    /// <param name="HandlerType">The structured handler type.</param>
    private sealed record LogEntry(
        LogLevel Level,
        Exception Exception,
        string Message,
        string OriginalFormat,
        string HandlerType);

    /// <summary>
    /// Provides a reusable no-op logging scope.
    /// </summary>
    private sealed class NullScope : IDisposable
    {
        /// <summary>
        /// Gets the singleton scope.
        /// </summary>
        public static NullScope Instance { get; } = new();

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
