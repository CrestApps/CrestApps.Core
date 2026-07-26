using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Populates <see cref="AICompletionContext.ToolInstanceNames"/> from the
/// <see cref="AIToolInstanceMetadata"/> stored on an <see cref="AIProfile"/>.
/// </summary>
internal sealed class AIToolInstanceCompletionContextBuilderHandler : IAICompletionContextBuilderHandler
{
    /// <summary>
    /// Copies the profile's configured tool instance names onto the completion context.
    /// </summary>
    /// <param name="context">The building context.</param>
    public Task BuildingAsync(AICompletionContextBuildingContext context)
    {
        if (context.Resource is AIProfile profile &&
            profile.TryGet<AIToolInstanceMetadata>(out var metadata) &&
            metadata.InstanceNames is { Length: > 0 })
        {
            context.Context.ToolInstanceNames = metadata.InstanceNames;
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
