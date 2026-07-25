using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Surfaces the configured <see cref="AIToolInstance"/> entries referenced by the completion context
/// to the tool registry. Each instance is materialized into a distinct <see cref="ToolRegistryEntry"/>
/// via its owning <see cref="IAIToolInstanceDefinition"/>, so multiple instances of the same
/// definition appear to the AI model as separate functions with their own descriptions.
/// </summary>
internal sealed class ToolInstanceRegistryProvider : IToolRegistryProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ToolInstanceRegistryProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolInstanceRegistryProvider"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve the catalog and definitions.</param>
    /// <param name="logger">The logger.</param>
    public ToolInstanceRegistryProvider(
        IServiceProvider serviceProvider,
        ILogger<ToolInstanceRegistryProvider> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Gets the tool entries for the configured instance identifiers on the completion context.
    /// </summary>
    /// <param name="context">The completion context that scopes available tools.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task<IReadOnlyList<ToolRegistryEntry>> GetToolsAsync(
        AICompletionContext context,
        CancellationToken cancellationToken = default)
    {
        var instanceIds = context?.ToolInstanceIds;

        if (instanceIds is null || instanceIds.Length == 0)
        {
            return [];
        }

        var catalog = _serviceProvider.GetService<ISourceCatalog<AIToolInstance>>();

        if (catalog is null)
        {
            return [];
        }

        var instances = await catalog.GetAsync(instanceIds, cancellationToken);

        if (instances.Count == 0)
        {
            return [];
        }

        var entries = new List<ToolRegistryEntry>();

        foreach (var instance in instances)
        {
            if (instance is null || string.IsNullOrEmpty(instance.Source))
            {
                continue;
            }

            var definition = _serviceProvider.GetKeyedService<IAIToolInstanceDefinition>(instance.Source);

            if (definition is null)
            {
                _logger.LogWarning(
                    "AI tool instance '{InstanceId}' references unknown definition '{Definition}'. Skipping.",
                    instance.ItemId, instance.Source);

                continue;
            }

            var functionName = AIToolInstanceNaming.GetFunctionName(instance);
            var description = !string.IsNullOrWhiteSpace(instance.Description)
                ? instance.Description
                : instance.DisplayText ?? functionName;
            var toolContext = new AIToolInstanceToolContext(instance, functionName, description);

            entries.Add(new ToolRegistryEntry
            {
                Id = $"tool-instance:{instance.ItemId}",
                Name = functionName,
                Description = description,
                Source = ToolRegistryEntrySource.Local,
                SourceId = instance.Source,
                CreateAsync = _ => ValueTask.FromResult(SafeCreate(definition, toolContext)),
            });
        }

        return entries;
    }

    private AITool SafeCreate(IAIToolInstanceDefinition definition, AIToolInstanceToolContext toolContext)
    {
        try
        {
            return definition.CreateTool(toolContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create tool for instance '{InstanceId}' from definition '{Definition}'.",
                toolContext.Instance.ItemId, definition.Name);

            return null;
        }
    }
}
