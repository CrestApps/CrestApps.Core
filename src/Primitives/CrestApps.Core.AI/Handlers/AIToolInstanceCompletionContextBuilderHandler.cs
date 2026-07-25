using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Populates <see cref="AICompletionContext.ToolInstanceIds"/> from the
/// <see cref="AIProfileToolInstanceMetadata"/> stored on an <see cref="AIProfile"/>.
/// </summary>
internal sealed class AIToolInstanceCompletionContextBuilderHandler : IAICompletionContextBuilderHandler
{
    /// <summary>
    /// Copies the profile's configured tool instance identifiers onto the completion context.
    /// </summary>
    /// <param name="context">The building context.</param>
    public Task BuildingAsync(AICompletionContextBuildingContext context)
    {
        if (context.Resource is AIProfile profile &&
            profile.TryGet<AIProfileToolInstanceMetadata>(out var metadata) &&
            metadata.InstanceIds is { Length: > 0 })
        {
            context.Context.ToolInstanceIds = metadata.InstanceIds;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// No-op once the context has been built.
    /// </summary>
    /// <param name="context">The built context.</param>
    public Task BuiltAsync(AICompletionContextBuiltContext context)
    {
        return Task.CompletedTask;
    }
}
