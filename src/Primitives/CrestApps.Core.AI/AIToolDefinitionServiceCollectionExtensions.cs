using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI;

/// <summary>
/// Service-collection extensions for registering the AI tool definition feature: parameterized,
/// user-configured tools built from developer-defined <see cref="AIToolSource"/> blueprints.
/// </summary>
public static class AIToolDefinitionServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core services required to configure and run AI tool definitions: the catalog
    /// handler, the completion-context builder handler, and the tool registry provider that surfaces
    /// configured definitions to the model. Call this once, then register one or more sources with
    /// <see cref="AddAIToolSource{TSource}(IServiceCollection)"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIToolDefinitions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ISourceCatalogManager<AIToolDefinition>>(sp => sp.GetRequiredService<SourceCatalogManager<AIToolDefinition>>());
        services.TryAddScoped<SourceCatalogManager<AIToolDefinition>>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<AIToolDefinition>, AIToolDefinitionCatalogHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAICompletionContextBuilderHandler, AIToolDefinitionCompletionContextBuilderHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IToolRegistryProvider, ToolDefinitionRegistryProvider>());

        return services;
    }

    /// <summary>
    /// Registers a developer-defined <see cref="AIToolSource"/> blueprint so users can create one or
    /// more configured <see cref="AIToolDefinition"/> entries from it and attach them to AI profiles.
    /// The source carries its own display metadata (name, description, category) and behavior, so no
    /// separate options, entry, or builder types are required.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddAIToolSource<TSource>(this IServiceCollection services)
        where TSource : AIToolSource
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCoreAIToolDefinitions();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<AIToolSource, TSource>());

        return services;
    }
}
