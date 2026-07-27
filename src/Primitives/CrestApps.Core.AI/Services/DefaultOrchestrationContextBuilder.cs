using System.Buffers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default implementation of <see cref="IOrchestrationContextBuilder"/> that runs the
/// registered <see cref="IOrchestrationContextBuilderHandler"/> pipeline.
/// </summary>
public sealed class DefaultOrchestrationContextBuilder : IOrchestrationContextBuilder
{
    private readonly IOrchestrationContextBuilderHandler[] _handlerArray;
    private readonly IEnumerable<IOrchestrationContextBuilderHandler> _handlers;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DefaultOrchestrationContextBuilder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultOrchestrationContextBuilder"/> class.
    /// </summary>
    /// <param name="handlers">The handlers.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="logger">The logger.</param>
    public DefaultOrchestrationContextBuilder(
        IEnumerable<IOrchestrationContextBuilderHandler> handlers,
        IServiceProvider serviceProvider,
        ILogger<DefaultOrchestrationContextBuilder> logger)
    {
        // Microsoft DI supplies an array; arbitrary enumerables retain the legacy lazy path.
        if (handlers is IOrchestrationContextBuilderHandler[] handlerArray)
        {
            _handlerArray = handlerArray;
            _handlers = [];
        }
        else
        {
            _handlers = handlers?.Reverse() ?? [];
        }

        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Builds the operation.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="configure">The configure.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<OrchestrationContext> BuildAsync(object resource, Action<OrchestrationContext> configure = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var context = new OrchestrationContext
        {
            ServiceProvider = _serviceProvider,
        };

        var building = new OrchestrationContextBuildingContext(resource, context);
        IOrchestrationContextBuilderHandler[] handlerBuffer = null;

        try
        {
            if (_handlerArray is not null)
            {
                if (_handlerArray.Length > 0)
                {
                    handlerBuffer = ArrayPool<IOrchestrationContextBuilderHandler>.Shared.Rent(_handlerArray.Length);
                    Array.Copy(_handlerArray, handlerBuffer, _handlerArray.Length);

                    for (var index = _handlerArray.Length - 1; index >= 0; index--)
                    {
                        var handler = handlerBuffer[index];

                        try
                        {
                            await handler.BuildingAsync(building, cancellationToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogError(ex, "Error in orchestration context building handler {Handler}.", handler.GetType().Name);
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
                        await handler.BuildingAsync(building, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Error in orchestration context building handler {Handler}.", handler.GetType().Name);
                    }
                }
            }

            configure?.Invoke(context);

            var built = new OrchestrationContextBuiltContext(resource, context);

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
                            await handler.BuiltAsync(built, cancellationToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogError(ex, "Error in orchestration context built handler {Handler}.", handler.GetType().Name);
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
                        await handler.BuiltAsync(built, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Error in orchestration context built handler {Handler}.", handler.GetType().Name);
                    }
                }
            }
        }
        finally
        {
            if (handlerBuffer is not null)
            {
                Array.Clear(handlerBuffer, 0, _handlerArray.Length);
                ArrayPool<IOrchestrationContextBuilderHandler>.Shared.Return(handlerBuffer);
            }
        }

        // Flush accumulated system message.
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
