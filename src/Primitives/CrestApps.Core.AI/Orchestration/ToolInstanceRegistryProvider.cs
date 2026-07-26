using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// The default <see cref="IToolRegistryProvider"/> that surfaces the configured
/// <see cref="AIToolInstance"/> entries referenced by the completion context to the tool registry. Each
/// instance is materialized into a distinct <see cref="ToolRegistryEntry"/> via its owning
/// <see cref="IAIToolInstanceSource"/> (resolved as a keyed service by <see cref="ISourceAwareModel.Source"/>),
/// so multiple instances built from the same source appear to the AI model as separate functions with
/// their own descriptions.
/// </summary>
/// <remarks>
/// Projects that need custom logic (for example, permission checks before exposing an instance) have two
/// options: register an additional <see cref="IToolRegistryProvider"/> alongside this one, or subclass this
/// provider and override <see cref="ShouldIncludeInstanceAsync"/> to filter instances while reusing the
/// entry-building logic. Register the subclass instead of the default (call
/// <c>AddToolInstances(useDefaultRegistry: false)</c> and register your provider) to fully control which
/// instances reach the model.
/// </remarks>
public class ToolInstanceRegistryProvider : IToolRegistryProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ToolInstanceRegistryProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolInstanceRegistryProvider"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve the instance catalog and sources.</param>
    /// <param name="logger">The logger.</param>
    public ToolInstanceRegistryProvider(
        IServiceProvider serviceProvider,
        ILogger<ToolInstanceRegistryProvider> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Gets the tool entries for the configured instance names on the completion context.
    /// </summary>
    /// <param name="context">The completion context that scopes available tools.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task<IReadOnlyList<ToolRegistryEntry>> GetToolsAsync(
        AICompletionContext context,
        CancellationToken cancellationToken = default)
    {
        var instanceNames = context?.ToolInstanceNames;

        if (instanceNames is null || instanceNames.Length == 0)
        {
            return [];
        }

        var catalog = _serviceProvider.GetService<INamedCatalog<AIToolInstance>>();

        if (catalog is null)
        {
            return [];
        }

        var entries = new List<ToolRegistryEntry>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var instanceName in instanceNames)
        {
            if (string.IsNullOrEmpty(instanceName) || !seenNames.Add(instanceName))
            {
                continue;
            }

            var instance = await catalog.FindByNameAsync(instanceName, cancellationToken);

            if (instance is null || string.IsNullOrEmpty(instance.Source))
            {
                continue;
            }

            if (!await ShouldIncludeInstanceAsync(instance, context, cancellationToken))
            {
                continue;
            }

            var source = _serviceProvider.GetKeyedService<IAIToolInstanceSource>(instance.Source);

            if (source is null)
            {
                _logger.LogWarning(
                    "AI tool instance '{InstanceName}' references unknown source '{Source}'. Skipping.",
                    instance.Name, instance.Source);

                continue;
            }

            var functionName = instance.GetFunctionName();
            var description = !string.IsNullOrWhiteSpace(instance.Description)
                ? instance.Description
                : functionName;

            entries.Add(new ToolRegistryEntry
            {
                Id = $"tool-instance:{instance.Name}",
                Name = functionName,
                Description = description,
                Source = ToolRegistryEntrySource.Local,
                SourceId = instance.Source,
                CreateAsync = _ => ValueTask.FromResult(SafeCreate(source, instance)),
            });
        }

        return entries;
    }

    /// <summary>
    /// Determines whether the resolved tool instance should be surfaced to the model for the current
    /// completion context. The default implementation includes every instance. Override this to apply
    /// custom rules such as per-user permission checks while reusing the built-in entry-building logic.
    /// </summary>
    /// <param name="instance">The resolved tool instance.</param>
    /// <param name="context">The completion context that scopes available tools.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> to include the instance; otherwise <see langword="false"/>.</returns>
    protected virtual ValueTask<bool> ShouldIncludeInstanceAsync(
        AIToolInstance instance,
        AICompletionContext context,
        CancellationToken cancellationToken)
        => ValueTask.FromResult(true);

    private AITool SafeCreate(IAIToolInstanceSource source, AIToolInstance instance)
    {
        try
        {
            return source.CreateTool(instance);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create tool for instance '{InstanceName}' from source '{Source}'.",
                instance.Name, instance.Source);

            return null;
        }
    }
}
