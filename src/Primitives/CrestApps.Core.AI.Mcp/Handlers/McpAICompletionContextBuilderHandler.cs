using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Mcp.Handlers;

internal sealed class McpAICompletionContextBuilderHandler : IAICompletionContextBuilderHandler
{
    /// <summary>
    /// Buildings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public Task BuildingAsync(AICompletionContextBuildingContext context)
    {
        if (context.Resource is AIProfile profile && profile.TryGet<AIProfileMcpMetadata>(out var mcpMetadata))
        {
            context.Context.McpConnectionIds = mcpMetadata.ConnectionIds;
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
