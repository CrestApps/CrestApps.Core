using System.Buffers;
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
    private readonly IAICompletionContextBuilderHandler[] _handlerArray;
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
        // Microsoft DI supplies an array; arbitrary enumerables retain the legacy lazy path.
        if (handlers is IAICompletionContextBuilderHandler[] handlerArray)
        {
            _handlerArray = handlerArray;
            _handlers = [];
        }
        else
        {
            _handlers = handlers?.Reverse() ?? [];
        }

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
        IAICompletionContextBuilderHandler[] handlerBuffer = null;

        try
        {
            if (_handlerArray is not null)
            {
                if (_handlerArray.Length > 0)
                {
                    handlerBuffer = ArrayPool<IAICompletionContextBuilderHandler>.Shared.Rent(_handlerArray.Length);
                    Array.Copy(_handlerArray, handlerBuffer, _handlerArray.Length);

                    for (var index = _handlerArray.Length - 1; index >= 0; index--)
                    {
                        var handler = handlerBuffer[index];

                        try
                        {
                            await handler.BuildingAsync(building);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogError(ex, "Error in completion context building handler {Handler}.", handler.GetType().Name);
                        }
                    }
                }
            }
            else
            {
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
            }

            configure?.Invoke(context);

            var built = new AICompletionContextBuiltContext(resource, context);

            if (_handlerArray is not null)
            {
                if (_handlerArray.Length > 0)
                {
                    Array.Copy(_handlerArray, handlerBuffer, _handlerArray.Length);

                    for (var index = _handlerArray.Length - 1; index >= 0; index--)
                    {
                        var handler = handlerBuffer[index];

                        try
                        {
                            await handler.BuiltAsync(built);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogError(ex, "Error in completion context built handler {Handler}.", handler.GetType().Name);
                        }
                    }
                }
            }
            else
            {
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
            }
        }
        finally
        {
            if (handlerBuffer is not null)
            {
                Array.Clear(handlerBuffer, 0, _handlerArray.Length);
                ArrayPool<IAICompletionContextBuilderHandler>.Shared.Return(handlerBuffer);
            }
        }

        return context;
    }
}
