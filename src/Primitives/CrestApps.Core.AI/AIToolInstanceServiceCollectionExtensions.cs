using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Services;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI;

/// <summary>
/// Service-collection extensions for registering the AI tool instance feature: parameterized,
/// user-configured tools built from developer-defined <see cref="IAIToolInstanceDefinition"/> types.
/// </summary>
public static class AIToolInstanceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core services required to configure and run AI tool instances: the catalog
    /// handler, the completion-context builder handler, and the tool registry provider that surfaces
    /// configured instances to the model. Call this once, then register one or more definitions with
    /// <see cref="AddAIToolInstanceDefinition{TDefinition}(IServiceCollection, string)"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIToolInstances(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<AIToolInstanceDefinitionOptions>();

        services.TryAddScoped<ISourceCatalogManager<AIToolInstance>>(sp => sp.GetRequiredService<SourceCatalogManager<AIToolInstance>>());
        services.TryAddScoped<SourceCatalogManager<AIToolInstance>>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<AIToolInstance>, AIToolInstanceCatalogHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAICompletionContextBuilderHandler, AIToolInstanceCompletionContextBuilderHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IToolRegistryProvider, ToolInstanceRegistryProvider>());

        return services;
    }

    /// <summary>
    /// Registers a developer-defined <see cref="IAIToolInstanceDefinition"/> so users can create one or
    /// more configured instances of it and attach them to AI profiles.
    /// </summary>
    /// <typeparam name="TDefinition">The definition type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The unique definition name. Stored as the source of every created instance.</param>
    /// <returns>A builder for configuring the definition's display metadata.</returns>
    public static AIToolInstanceDefinitionBuilder<TDefinition> AddAIToolInstanceDefinition<TDefinition>(
        this IServiceCollection services,
        string name)
        where TDefinition : class, IAIToolInstanceDefinition
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);

        services.AddCoreAIToolInstances();

        services.AddSingleton<TDefinition>();
        services.AddKeyedSingleton<IAIToolInstanceDefinition>(name, (sp, _) => sp.GetRequiredService<TDefinition>());

        var entry = new AIToolInstanceDefinitionEntry
        {
            Name = name,
        };

        services.Configure<AIToolInstanceDefinitionOptions>(options =>
        {
            entry.DisplayName ??= new LocalizedString(name, name);
            entry.Description ??= new LocalizedString(name, name);

            options.SetDefinition(name, entry);
        });

        return new AIToolInstanceDefinitionBuilder<TDefinition>(entry);
    }
}
