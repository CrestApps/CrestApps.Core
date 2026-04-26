using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default implementation of <see cref="IAICompletionContextBuilder"/> that runs the
/// registered <see cref="IAICompletionContextBuilderHandler"/> pipeline.
/// </summary>
public sealed class DefaultAICompletionContextBuilder : IAICompletionContextBuilder
{
    private readonly IEnumerable<IAICompletionContextBuilderHandler> _handlers;
    private readonly ILogger<DefaultAICompletionContextBuilder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAICompletionContextBuilder"/> class.
    /// </summary>
    /// <param name="handlers">The handlers.</param>
    /// <param name="logger">The logger.</param>
    public DefaultAICompletionContextBuilder(
        IEnumerable<IAICompletionContextBuilderHandler> handlers,
        ILogger<DefaultAICompletionContextBuilder> logger)
    {
        _handlers = handlers?.Reverse() ?? [];
        _logger = logger;
    }

    /// <summary>
    /// Builds the operation.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="configure">The configure.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<AICompletionContext> BuildAsync(object resource, Action<AICompletionContext> configure = null, CancellationToken cancellationToken = default)
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
