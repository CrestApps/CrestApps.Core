using CrestApps.Core.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Filters;

/// <summary>
/// A global MVC action filter that commits all staged store writes after each controller
/// action completes successfully. The filter runs as the outermost action filter so that
/// it wraps all other action filters and sees their results before committing.
/// Register via <see cref="ServiceCollectionExtensions.AddStoreCommitterFilter(IMvcBuilder)"/>.
/// </summary>
public sealed class StoreCommitterActionFilter : IAsyncActionFilter, IOrderedFilter
{
    private readonly IStoreCommitter _committer;
    private readonly ILogger<StoreCommitterActionFilter> _logger;

    public StoreCommitterActionFilter(IStoreCommitter committer, ILogger<StoreCommitterActionFilter> logger)
    {
        _committer = committer;
        _logger = logger;
    }

    /// <summary>
    /// Outermost position so this filter wraps all other action filters.
    /// </summary>
    public int Order => int.MaxValue - 100;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executedContext = await next();

        if (executedContext.Exception is null || executedContext.ExceptionHandled)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("StoreCommitterActionFilter committing after MVC action '{ActionName}'.", context.ActionDescriptor.DisplayName);
            }

            await _committer.CommitAsync(context.HttpContext.RequestAborted);
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("StoreCommitterActionFilter not committing after MVC action '{ActionName}' due to an exception.", context.ActionDescriptor.DisplayName);
        }
    }
}
