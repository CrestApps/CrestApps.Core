using CrestApps.Core.Builders;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI.Indexing;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds core indexing services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreIndexingServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ISearchIndexProfileStore, NullSearchIndexProfileStore>();
        services.TryAddScoped<ICatalog<SearchIndexProfile>>(sp => sp.GetRequiredService<ISearchIndexProfileStore>());
        services.TryAddScoped<INamedCatalog<SearchIndexProfile>>(sp => sp.GetRequiredService<ISearchIndexProfileStore>());
        services.TryAddScoped<ISearchIndexProfileManager, SearchIndexProfileManager>();
        services.TryAddScoped<ICatalogManager<SearchIndexProfile>>(sp => sp.GetRequiredService<ISearchIndexProfileManager>());
        services.TryAddScoped<ISearchIndexProfileProvisioningService, SearchIndexProfileProvisioningService>();

return services;
    }

    /// <summary>
    /// Adds core indexing services.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsIndexingBuilder AddCoreIndexingServices(this CrestAppsIndexingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreIndexingServices();

return builder;
    }

    /// <summary>
    /// Adds core ai data source index profile handler.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIDataSourceIndexProfileHandler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIndexProfileHandler, DataSourceSearchIndexProfileHandler>());

return services;
    }

    /// <summary>
    /// Adds core ai memory index profile handler.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIMemoryIndexProfileHandler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIndexProfileHandler, AIMemorySearchIndexProfileHandler>());

return services;
    }

    /// <summary>
    /// Adds core ai default index profile handler.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIDefaultIndexProfileHandler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIndexProfileHandler, DefaultSearchIndexProfileHandler>());

return services;
    }
}
