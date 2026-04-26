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

    public static CrestAppsIndexingBuilder AddCoreIndexingServices(this CrestAppsIndexingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreIndexingServices();

        return builder;
    }

    public static IServiceCollection AddCoreAIDataSourceIndexProfileHandler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIndexProfileHandler, DataSourceSearchIndexProfileHandler>());

        return services;
    }

    public static IServiceCollection AddCoreAIMemoryIndexProfileHandler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIndexProfileHandler, AIMemorySearchIndexProfileHandler>());

        return services;
    }

    public static IServiceCollection AddCoreAIDefaultIndexProfileHandler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIndexProfileHandler, DefaultSearchIndexProfileHandler>());

        return services;
    }
}
