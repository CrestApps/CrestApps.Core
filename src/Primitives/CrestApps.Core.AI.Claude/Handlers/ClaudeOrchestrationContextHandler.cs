using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;

namespace CrestApps.Core.AI.Claude.Handlers;

internal sealed class ClaudeOrchestrationContextHandler : IOrchestrationContextBuilderHandler
{
    /// <summary>
    /// Buildings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public Task BuildingAsync(OrchestrationContextBuildingContext context)
    {
        if (context.Resource is not ExtensibleEntity entity)
        {
            return Task.CompletedTask;
        }

        if (entity.TryGet<ClaudeSessionMetadata>(out var metadata))
        {
            context.Context.Properties[nameof(ClaudeSessionMetadata)] = metadata;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builts the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public Task BuiltAsync(OrchestrationContextBuiltContext context)
    {
        return Task.CompletedTask;
    }
}
