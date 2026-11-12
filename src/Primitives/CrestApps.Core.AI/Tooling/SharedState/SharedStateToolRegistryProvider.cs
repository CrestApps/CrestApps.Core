using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling.SharedState;

/// <summary>
/// Offers the <see cref="UpdateSharedStateFunction"/> tool to a chat run whose client shares a state with the agent.
/// </summary>
internal sealed class SharedStateToolRegistryProvider : IToolRegistryProvider
{
    /// <summary>
    /// Gets the shared state tool when the completion has a shared state.
    /// </summary>
    /// <param name="context">The completion context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tool entry, or none.</returns>
    public Task<IReadOnlyList<ToolRegistryEntry>> GetToolsAsync(AICompletionContext context, CancellationToken cancellationToken = default)
    {
        if (context is null ||
            context.DisableTools ||
            !context.AdditionalProperties.TryGetValue(AICompletionContextKeys.SharedState, out var value) ||
            value is not ChatSharedState state)
        {
            return Task.FromResult<IReadOnlyList<ToolRegistryEntry>>([]);
        }

        IReadOnlyList<ToolRegistryEntry> entries =
        [
            new ToolRegistryEntry
            {
                Id = UpdateSharedStateFunction.FunctionName,
                Name = UpdateSharedStateFunction.FunctionName,
                Description = "Changes the state shared with the user.",
                Source = ToolRegistryEntrySource.System,
                CreateAsync = _ => ValueTask.FromResult<AITool>(new UpdateSharedStateFunction(state)),
            },
        ];

        return Task.FromResult(entries);
    }
}
