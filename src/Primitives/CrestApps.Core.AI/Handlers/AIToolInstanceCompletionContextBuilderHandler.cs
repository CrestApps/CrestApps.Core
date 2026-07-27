using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Populates <see cref="AICompletionContext.ToolInstanceNames"/> from the
/// <see cref="AIToolInstanceMetadata"/> stored on the resource driving the completion. Because the
/// metadata lives in the resource's <see cref="ExtensibleEntity.Properties"/> bag, this handler works for
/// any resource that carries it — for example an <see cref="AIProfile"/> or a <c>ChatInteraction</c> — so
/// configured tool instances are honored across every orchestrator, not just a single feature.
/// </summary>
internal sealed class AIToolInstanceCompletionContextBuilderHandler : IAICompletionContextBuilderHandler
{
    /// <summary>
    /// Copies the resource's configured tool instance names onto the completion context.
    /// </summary>
    /// <param name="context">The building context.</param>
    public Task BuildingAsync(AICompletionContextBuildingContext context)
    {
        if (context.Resource is ExtensibleEntity entity &&
            entity.TryGet<AIToolInstanceMetadata>(out var metadata) &&
            metadata.ToolInstanceNames is { Length: > 0 })
        {
            context.Context.ToolInstanceNames = metadata.ToolInstanceNames;
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
