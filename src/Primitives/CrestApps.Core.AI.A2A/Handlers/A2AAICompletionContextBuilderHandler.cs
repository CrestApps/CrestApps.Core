using CrestApps.Core.AI.A2A.Models;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.A2A.Handlers;

internal sealed class A2AAICompletionContextBuilderHandler : IAICompletionContextBuilderHandler
{
    /// <summary>
    /// Buildings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public Task BuildingAsync(AICompletionContextBuildingContext context)
    {
        if (context.Resource is AIProfile profile && profile.TryGet<AIProfileA2AMetadata>(out var a2aMetadata))
        {
            context.Context.A2AConnectionIds = a2aMetadata.ConnectionIds;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builts the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public Task BuiltAsync(AICompletionContextBuiltContext context)
    {
        return Task.CompletedTask;
    }
}
