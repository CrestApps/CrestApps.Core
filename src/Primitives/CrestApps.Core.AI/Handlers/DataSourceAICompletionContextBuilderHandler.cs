using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;

namespace CrestApps.Core.AI.Handlers;

internal sealed class DataSourceAICompletionContextBuilderHandler : IAICompletionContextBuilderHandler
{
    /// <summary>
    /// Buildings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public Task BuildingAsync(AICompletionContextBuildingContext context)
    {
        if (context.Resource is AIProfile profile && profile.TryGet<DataSourceMetadata>(out var dataSourceMetadata) && !string.IsNullOrEmpty(dataSourceMetadata.DataSourceId))
        {
            context.Context.DataSourceId = dataSourceMetadata.DataSourceId;
            // Store DataSourceId in the invocation context so the DataSourceSearchTool can access it.
            var invocationContext = AIInvocationScope.Current;
            invocationContext?.DataSourceId = dataSourceMetadata.DataSourceId;
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
