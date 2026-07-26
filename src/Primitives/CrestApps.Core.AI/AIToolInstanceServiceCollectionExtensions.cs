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
    /// Registers the core services required to configure and run AI tool instances: the catalog handler
    /// and the completion-context builder handler. This does <b>not</b> register a tool registry provider;
    /// call <see cref="AddDefaultAIToolInstanceRegistry"/> for the built-in provider, or register your own
    /// <see cref="IToolRegistryProvider"/> to control which instances are surfaced to the model.
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

        return services;
    }

    /// <summary>
    /// Registers the built-in <see cref="ToolInstanceRegistryProvider"/> that surfaces the configured
    /// <see cref="AIToolInstance"/> entries named on the completion context to the orchestrator. Registered
    /// additively, so it is safe to call more than once. To take full control of which instances are
    /// exposed (for example to enforce per-user permissions), skip this and register your own
    /// <see cref="IToolRegistryProvider"/> instead — see the documentation for an example.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddDefaultAIToolInstanceRegistry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

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
    /// <param name="useDefaultRegistry">
    /// When <see langword="true"/> (the default), also registers the built-in
    /// <see cref="ToolInstanceRegistryProvider"/> via <see cref="AddDefaultAIToolInstanceRegistry"/>. Pass
    /// <see langword="false"/> to supply your own <see cref="IToolRegistryProvider"/> instead.
    /// </param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddAIToolInstanceSource<TSource>(
        this IServiceCollection services,
        string name,
        Action<AIToolInstanceSourceEntry> configure = null,
        bool useDefaultRegistry = true)
        where TSource : class, IAIToolInstanceSource
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(name);

        services.AddCoreAIToolInstances();

        if (useDefaultRegistry)
        {
            services.AddDefaultAIToolInstanceRegistry();
        }

        services.TryAddKeyedScoped<IAIToolInstanceSource, TSource>(name);

        services.Configure<AIOptions>(options =>
        {
            options.AddToolInstanceSource(name, configure);
        });

        return services;
    }
}
