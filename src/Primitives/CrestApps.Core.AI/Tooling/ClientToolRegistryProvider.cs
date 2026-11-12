using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Offers the tools a client runs itself (such as the frontend tools of an AG-UI client) to the model, as
/// declarations: the function invocation does not run them, so a call ends the request and the client answers it.
/// </summary>
internal sealed class ClientToolRegistryProvider : IToolRegistryProvider
{
    /// <summary>
    /// Gets the client's tools when the completion has any.
    /// </summary>
    /// <param name="context">The completion context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tool entries, or none.</returns>
    public Task<IReadOnlyList<ToolRegistryEntry>> GetToolsAsync(AICompletionContext context, CancellationToken cancellationToken = default)
    {
        if (context is null ||
            context.DisableTools ||
            !context.AdditionalProperties.TryGetValue(AICompletionContextKeys.ClientTools, out var value) ||
            value is not IReadOnlyList<AITool> tools ||
            tools.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ToolRegistryEntry>>([]);
        }

        IReadOnlyList<ToolRegistryEntry> entries = tools
            .Where(tool => !string.IsNullOrWhiteSpace(tool.Name))
            .Select(tool => new ToolRegistryEntry
            {
                Id = $"client:{tool.Name}",
                Name = tool.Name,
                Description = tool.Description,
                Source = ToolRegistryEntrySource.Local,
                SourceId = "client",
                CreateAsync = _ => ValueTask.FromResult(tool),
            })
            .ToList();

        return Task.FromResult(entries);
    }
}
