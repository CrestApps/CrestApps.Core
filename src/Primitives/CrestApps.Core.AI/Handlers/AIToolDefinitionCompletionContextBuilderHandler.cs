using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Populates <see cref="AICompletionContext.ToolDefinitionIds"/> from the
/// <see cref="AIProfileToolDefinitionMetadata"/> stored on an <see cref="AIProfile"/>.
/// </summary>
internal sealed class AIToolDefinitionCompletionContextBuilderHandler : IAICompletionContextBuilderHandler
{
    /// <summary>
    /// Copies the profile's configured tool definition identifiers onto the completion context.
    /// </summary>
    /// <param name="context">The building context.</param>
    public Task BuildingAsync(AICompletionContextBuildingContext context)
    {
        if (context.Resource is AIProfile profile &&
            profile.TryGet<AIProfileToolDefinitionMetadata>(out var metadata) &&
            metadata.DefinitionIds is { Length: > 0 })
        {
            context.Context.ToolDefinitionIds = metadata.DefinitionIds;
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
