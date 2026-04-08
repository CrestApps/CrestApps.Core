#nullable enable
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
/// <c>ChannelReader</c>) must call <see cref="IStoreCommitter.CommitAsync"/> explicitly
/// from inside the async lambda, because the hub method returns before the work
/// completes and this filter commits too early for those cases.
/// </remarks>
public sealed class StoreCommitterHubFilter : IHubFilter
{
    private readonly ILogger<StoreCommitterHubFilter> _logger;

    public StoreCommitterHubFilter(ILogger<StoreCommitterHubFilter> logger)
    {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var result = await next(invocationContext);

        var committer = invocationContext.ServiceProvider.GetRequiredService<IStoreCommitter>();
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("StoreCommitterHubFilter committing after hub method '{HubMethod}' on hub '{HubName}'.", invocationContext.HubMethodName, invocationContext.Hub.GetType().Name);
        }

        await committer.CommitAsync();

        return result;
    }

    public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        return next(context);
    }

    public Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        return next(context, exception);
    }
}
