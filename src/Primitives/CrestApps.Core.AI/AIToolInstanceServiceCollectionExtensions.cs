using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI;

/// <summary>
/// Service-collection extensions for registering the AI tool instance feature: parameterized,
/// user-configured tools built from developer-defined <see cref="IAIToolInstanceSource"/> blueprints.
/// </summary>
public static class AIToolInstanceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core services required to configure and run AI tool instances: the catalog handler,
    /// the completion-context builder handler, and the default tool registry provider that surfaces
    /// configured instances to the model. Call this once, then register one or more sources with
    /// <see cref="AddAIToolInstanceSource{TSource}(IServiceCollection, string, Action{AIToolInstanceSourceEntry})"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCoreAIToolInstances(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ISourceCatalogManager<AIToolInstance>>(sp => sp.GetRequiredService<SourceCatalogManager<AIToolInstance>>());
        services.TryAddScoped<SourceCatalogManager<AIToolInstance>>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<AIToolInstance>, AIToolInstanceCatalogHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAICompletionContextBuilderHandler, AIToolInstanceCompletionContextBuilderHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IToolRegistryProvider, ToolInstanceRegistryProvider>());

        return services;
    }

    /// <summary>
    /// Registers a developer-defined <see cref="IAIToolInstanceSource"/> blueprint so users can create one
    /// or more configured <see cref="AIToolInstance"/> entries from it and attach them to AI profiles.
    /// The source's display metadata (display name, description, category) is recorded in
    /// <see cref="AIOptions.ToolInstanceSources"/>, while the behavior is registered as a keyed service
    /// resolved by the source name.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">The unique registered name of the source. Stored as the source of every instance created from it.</param>
    /// <param name="configure">An optional delegate used to configure the source display metadata.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddAIToolInstanceSource<TSource>(
        this IServiceCollection services,
        string name,
        Action<AIToolInstanceSourceEntry> configure = null)
        where TSource : class, IAIToolInstanceSource
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);

        services.AddCoreAIToolInstances();

        services.TryAddKeyedScoped<IAIToolInstanceSource, TSource>(name);

        services.Configure<AIOptions>(options =>
        {
            options.AddToolInstanceSource(name, configure);
        });

        return services;
    }
}
