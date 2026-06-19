using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Provides profile-selected tools from <see cref="AIToolDefinitionOptions"/>
/// to the tool registry. Dependency expansion is handled centrally by the
/// tool registry coordinator after all providers have contributed their entries.
/// </summary>
internal sealed class ProfileToolRegistryProvider : IToolRegistryProvider
{
    private readonly IOptions<AIToolDefinitionOptions> _toolDefinitions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileToolRegistryProvider"/> class.
    /// </summary>
    /// <param name="toolDefinitions">The tool definitions.</param>
    public ProfileToolRegistryProvider(IOptions<AIToolDefinitionOptions> toolDefinitions)
    {
        _toolDefinitions = toolDefinitions;
    }

    /// <summary>
    /// Gets tools.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<IReadOnlyList<ToolRegistryEntry>> GetToolsAsync(
        AICompletionContext context,
        CancellationToken cancellationToken = default)
    {
        var configuredToolNames = context?.ToolNames;

        if (configuredToolNames is null || configuredToolNames.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<ToolRegistryEntry>>([]);
        }

        var toolDefinitions = _toolDefinitions.Value.Tools;
        var entries = new List<ToolRegistryEntry>();

        foreach (var toolName in configuredToolNames)
        {
            if (!toolDefinitions.TryGetValue(toolName, out var definition))
            {
                continue;
            }

            if (definition.IsSystemTool)
            {
                continue;
            }

            entries.Add(CreateEntry(toolName, definition));
        }

        return Task.FromResult<IReadOnlyList<ToolRegistryEntry>>(entries);
    }

    private static ToolRegistryEntry CreateEntry(string toolName, AIToolDefinitionEntry definition)
    {
        return new ToolRegistryEntry
        {
            Id = toolName,
            Name = toolName,
            Description = definition.Description ?? definition.Title ?? toolName,
            Source = definition.IsSystemTool
                ? ToolRegistryEntrySource.System
                : ToolRegistryEntrySource.Local,
            CreateAsync = (sp) => ValueTask.FromResult(sp.GetKeyedService<AITool>(toolName)),
        };
    }
}
