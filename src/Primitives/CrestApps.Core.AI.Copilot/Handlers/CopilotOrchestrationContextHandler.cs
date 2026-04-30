using CrestApps.Core.AI.Copilot.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;

namespace CrestApps.Core.AI.Copilot.Handlers;

/// <summary>
/// Reads <see cref="CopilotSessionMetadata"/> from the resource (AIProfile or ChatInteraction)
/// and sets it on <see cref="OrchestrationContext.Properties"/> so the CopilotOrchestrator
/// can read the model name and flags without coupling through <see cref="AICompletionContext"/>.
/// </summary>
internal sealed class CopilotOrchestrationContextHandler : IOrchestrationContextBuilderHandler
{
    /// <summary>
    /// Buildings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task BuildingAsync(OrchestrationContextBuildingContext context, CancellationToken cancellationToken = default)
    {
        if (context.Resource is not ExtensibleEntity entity)
        {
            return Task.CompletedTask;
        }

        if (entity.TryGet<CopilotSessionMetadata>(out var metadata))
        {
            context.Context.Properties[nameof(CopilotSessionMetadata)] = metadata;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builts the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task BuiltAsync(OrchestrationContextBuiltContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
