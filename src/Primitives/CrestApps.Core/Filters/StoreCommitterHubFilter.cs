using System.Threading.Channels;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Filters;

/// <summary>
/// A SignalR hub filter that commits all staged store writes after each hub method
/// completes. The filter resolves <see cref="IStoreCommitter"/> from the per-invocation
/// service scope so it targets the same store session that the hub method used.
/// Register via
/// <see cref="ServiceCollectionExtensions.AddStoreCommitterFilter(ISignalRServerBuilder)"/>.
/// </summary>
/// <remarks>
/// Hub methods that perform fire-and-forget async work (e.g., streaming via
/// <c>ChannelReader&lt;T&gt;</c> or <c>IAsyncEnumerable&lt;T&gt;</c>) must call
/// <see cref="IStoreCommitter.CommitAsync"/> explicitly from inside the async lambda,
/// because the hub method returns before the work completes. This filter detects such
/// streaming results and skips committing for them, so it never commits concurrently
/// with the still-running background task on the same non-thread-safe store session.
/// </remarks>
public sealed class StoreCommitterHubFilter : IHubFilter
{
    private readonly ILogger<StoreCommitterHubFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreCommitterHubFilter"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public StoreCommitterHubFilter(ILogger<StoreCommitterHubFilter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Invokes method.
    /// </summary>
    /// <param name="invocationContext">The invocation context.</param>
    /// <param name="next">The next.</param>
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var result = await next(invocationContext);

        // Streaming hub methods (returning ChannelReader<T> or IAsyncEnumerable<T>) run their
        // work fire-and-forget and return before it completes. They commit their own staged
        // writes from inside the async lambda. Committing here would run concurrently with that
        // still-running background task on the same non-thread-safe store session, which can
        // drop or corrupt writes (the race only manifests when the background work completes
        // quickly, e.g. against a fast completion provider). Skip the commit for those results.
        if (IsStreamingResult(result))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("StoreCommitterHubFilter skipping commit after streaming hub method '{HubMethod}' on hub '{HubName}' (result type '{ResultType}'); the streaming method self-commits.", invocationContext.HubMethodName, invocationContext.Hub.GetType().Name, result?.GetType().Name ?? "null");
            }

            return result;
        }

        var committer = invocationContext.ServiceProvider.GetRequiredService<IStoreCommitter>();
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("StoreCommitterHubFilter committing after hub method '{HubMethod}' on hub '{HubName}' using committer {CommitterHash} from services scope {ServicesHash} (result type '{ResultType}').", invocationContext.HubMethodName, invocationContext.Hub.GetType().Name, committer.GetHashCode(), invocationContext.ServiceProvider.GetHashCode(), result?.GetType().Name ?? "null");
        }

        await committer.CommitAsync();

        return result;
    }

    private static bool IsStreamingResult(object? result)
    {
        if (result is null)
        {
            return false;
        }

        var type = result.GetType();

        foreach (var interfaceType in type.GetInterfaces())
        {
            if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
            {
                return true;
            }
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ChannelReader<>))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Ons connected.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="next">The next.</param>
    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        return next(context);
    }

    /// <summary>
    /// Ons disconnected.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="exception">The exception.</param>
    /// <param name="next">The next.</param>
    public Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        return next(context, exception);
    }
}
