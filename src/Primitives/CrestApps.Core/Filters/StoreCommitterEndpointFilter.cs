#nullable enable
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Filters;

/// <summary>
/// A Minimal API endpoint filter that commits all staged store writes after each endpoint
/// handler completes successfully. Apply it to a route group or individual endpoints via
/// <c>AddEndpointFilter&lt;StoreCommitterEndpointFilter&gt;()</c>.
/// </summary>
public sealed class StoreCommitterEndpointFilter : IEndpointFilter
{
    private readonly IStoreCommitter _committer;
    private readonly ILogger<StoreCommitterEndpointFilter> _logger;

    public StoreCommitterEndpointFilter(IStoreCommitter committer, ILogger<StoreCommitterEndpointFilter> logger)
    {
        _committer = committer;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("StoreCommitterEndpointFilter committing after endpoint '{EndpointName}'.", context.HttpContext.GetEndpoint()?.DisplayName);
        }

        await _committer.CommitAsync(context.HttpContext.RequestAborted);

        return result;
    }
}
