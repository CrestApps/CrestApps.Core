using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Surfaces the configured <see cref="AIToolDefinition"/> entries referenced by the completion context
/// to the tool registry. Each definition is materialized into a distinct <see cref="ToolRegistryEntry"/>
/// via its owning <see cref="AIToolSource"/>, so multiple definitions built from the same source appear
/// to the AI model as separate functions with their own descriptions.
/// </summary>
internal sealed class ToolDefinitionRegistryProvider : IToolRegistryProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, AIToolSource> _sources;
    private readonly ILogger<ToolDefinitionRegistryProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolDefinitionRegistryProvider"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve the definition catalog.</param>
    /// <param name="sources">The registered tool sources, keyed by <see cref="AIToolSource.Name"/>.</param>
    /// <param name="logger">The logger.</param>
    public ToolDefinitionRegistryProvider(
        IServiceProvider serviceProvider,
        IEnumerable<AIToolSource> sources,
        ILogger<ToolDefinitionRegistryProvider> logger)
    {
        _serviceProvider = serviceProvider;
        _sources = BuildSourceLookup(sources);
        _logger = logger;
    }

    /// <summary>
    /// Gets the tool entries for the configured definition identifiers on the completion context.
    /// </summary>
    /// <param name="context">The completion context that scopes available tools.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task<IReadOnlyList<ToolRegistryEntry>> GetToolsAsync(
        AICompletionContext context,
        CancellationToken cancellationToken = default)
    {
        var definitionIds = context?.ToolDefinitionIds;

        if (definitionIds is null || definitionIds.Length == 0)
        {
            return [];
        }

        var catalog = _serviceProvider.GetService<ISourceCatalog<AIToolDefinition>>();

        if (catalog is null)
        {
            return [];
        }

        var definitions = await catalog.GetAsync(definitionIds, cancellationToken);

        if (definitions.Count == 0)
        {
            return [];
        }

        var entries = new List<ToolRegistryEntry>();

        foreach (var definition in definitions)
        {
            if (definition is null || string.IsNullOrEmpty(definition.Source))
            {
                continue;
            }

            if (!_sources.TryGetValue(definition.Source, out var source))
            {
                _logger.LogWarning(
                    "AI tool definition '{DefinitionId}' references unknown source '{Source}'. Skipping.",
                    definition.ItemId, definition.Source);

                continue;
            }

            var functionName = AIToolDefinitionNaming.GetFunctionName(definition);
            var description = !string.IsNullOrWhiteSpace(definition.Description)
                ? definition.Description
                : definition.DisplayText ?? functionName;
            var toolContext = new AIToolSourceContext(definition, functionName, description);

            entries.Add(new ToolRegistryEntry
            {
                Id = $"tool-definition:{definition.ItemId}",
                Name = functionName,
                Description = description,
                Source = ToolRegistryEntrySource.Local,
                SourceId = definition.Source,
                CreateAsync = _ => ValueTask.FromResult(SafeCreate(source, toolContext)),
            });
        }

        return entries;
    }

    private static Dictionary<string, AIToolSource> BuildSourceLookup(IEnumerable<AIToolSource> sources)
    {
        var lookup = new Dictionary<string, AIToolSource>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            if (source is null || string.IsNullOrEmpty(source.Name))
            {
                continue;
            }

            lookup[source.Name] = source;
        }

        return lookup;
    }

    private AITool SafeCreate(AIToolSource source, AIToolSourceContext toolContext)
    {
        try
        {
            return source.CreateTool(toolContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create tool for definition '{DefinitionId}' from source '{Source}'.",
                toolContext.Definition.ItemId, source.Name);

            return null;
        }
    }
}
