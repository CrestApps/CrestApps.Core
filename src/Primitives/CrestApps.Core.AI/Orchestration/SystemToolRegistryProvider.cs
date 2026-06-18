using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Provides system tools to the tool registry. System tools are automatically
/// included by the orchestrator based on context availability and are not user-selectable.
/// </summary>
/// <remarks>
/// Tools tagged with <see cref="AIToolPurposes.DataSourceSearch"/> are only included
/// when <see cref="AICompletionContext.DataSourceId"/> is set (a data source is attached).
/// Tools tagged with <see cref="AIToolPurposes.DocumentProcessing"/> are only included
/// when the context signals that documents are available via the
/// <see cref="AICompletionContextKeys.HasDocuments"/> additional property.
/// </remarks>
internal sealed class SystemToolRegistryProvider : IToolRegistryProvider
{
    private readonly AIToolDefinitionOptions _toolOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemToolRegistryProvider"/> class.
    /// </summary>
    /// <param name="toolOptions">The tool options.</param>
    public SystemToolRegistryProvider(IOptions<AIToolDefinitionOptions> toolOptions)
    {
        _toolOptions = toolOptions.Value;
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
        var explicitlyRequestedToolNames = context?.ToolNames is { Length: > 0 }
            ? new HashSet<string>(_toolOptions.ExpandToolNames(context.ToolNames), StringComparer.OrdinalIgnoreCase)
            : [];
        var hasDataSource = !string.IsNullOrEmpty(context?.DataSourceId);
        var hasDocuments = context?.AdditionalProperties is not null
            && context.AdditionalProperties.TryGetValue(AICompletionContextKeys.HasDocuments, out var val)
                && val is true;
        var hasMemory = context?.AdditionalProperties is not null
            && context.AdditionalProperties.TryGetValue(AICompletionContextKeys.HasMemory, out var hasMemoryValue)
                && hasMemoryValue is true;

        var selectedToolNames = new List<string>();

        foreach (var (name, entry) in _toolOptions.Tools)
        {
            if (!entry.IsSystemTool || explicitlyRequestedToolNames.Contains(name))
            {
                continue;
            }

            if (!IsAvailableInContext(entry, hasDataSource, hasDocuments, hasMemory))
            {
                continue;
            }

            selectedToolNames.Add(name);
        }

        var entries = new List<ToolRegistryEntry>();

        foreach (var toolName in _toolOptions.ExpandToolNames(selectedToolNames))
        {
            if (explicitlyRequestedToolNames.Contains(toolName) ||
                !_toolOptions.Tools.TryGetValue(toolName, out var definition))
            {
                continue;
            }

            entries.Add(CreateEntry(toolName, definition));
        }

        return Task.FromResult<IReadOnlyList<ToolRegistryEntry>>(entries);
    }

    private static bool IsAvailableInContext(
        AIToolDefinitionEntry entry,
        bool hasDataSource,
        bool hasDocuments,
        bool hasMemory)
    {
        if (entry.HasPurpose(AIToolPurposes.DataSourceSearch) && !hasDataSource)
        {
            return false;
        }

        if (entry.HasPurpose(AIToolPurposes.DocumentProcessing) && !hasDocuments)
        {
            return false;
        }

        if (entry.HasPurpose(AIToolPurposes.Memory) && !hasMemory)
        {
            return false;
        }

        return true;
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
