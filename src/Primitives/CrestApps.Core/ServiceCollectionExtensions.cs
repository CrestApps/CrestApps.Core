using CrestApps.Core.Builders;
using CrestApps.Core.Filters;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrestApps.Core;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds crest apps core.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The action used to configure.</param>
    public static IServiceCollection AddCrestAppsCore(this IServiceCollection services, Action<CrestAppsCoreBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<ExtensibleEntityJsonOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ExtensibleEntityJsonOptionsInitializer>());

        configure(new CrestAppsCoreBuilder(services));

return services;
    }

    /// <summary>
    /// Adds indexing services.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">The configure.</param>
    public static CrestAppsCoreBuilder AddIndexingServices(this CrestAppsCoreBuilder builder, Action<CrestAppsIndexingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        configure(new CrestAppsIndexingBuilder(builder.Services));

return builder;
    }

    /// <summary>
    /// Adds core services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IODataValidator, ODataFilterValidator>();

return services;
    }

    /// <summary>
    /// Adds catalog managers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCatalogManagers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped(typeof(ICatalogManager<>), typeof(CatalogManager<>));
        services.TryAddScoped(typeof(INamedCatalogManager<>), typeof(NamedCatalogManager<>));
        services.TryAddScoped(typeof(ISourceCatalogManager<>), typeof(SourceCatalogManager<>));
        services.TryAddScoped(typeof(INamedSourceCatalogManager<>), typeof(NamedSourceCatalogManager<>));

return services;
    }

    /// <summary>
    /// Registers <see cref="StoreCommitterActionFilter"/> as a global MVC action filter.
    /// The filter commits all staged store writes after each controller action completes
    /// successfully. Call this on your <see cref="IMvcBuilder"/> after registering a
    /// store package (e.g., <c>AddCoreYesSqlDataStore</c>) that provides
    /// <see cref="IStoreCommitter"/>.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static IMvcBuilder AddCrestAppsStoreCommitterFilter(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddScoped<StoreCommitterActionFilter>();
        builder.Services.Configure<MvcOptions>(options => options.Filters.AddService<StoreCommitterActionFilter>());

        return builder;
    }

    /// <summary>
    /// Registers <see cref="StoreCommitterHubFilter"/> as a global SignalR hub filter.
    /// The filter commits all staged store writes after each hub method completes.
    /// Call this on your <see cref="ISignalRServerBuilder"/> after registering a store
    /// package that provides <see cref="IStoreCommitter"/>.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static ISignalRServerBuilder AddCrestAppsStoreCommitterFilter(this ISignalRServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<StoreCommitterHubFilter>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<HubOptions>, StoreCommitterHubFilterOptionsSetup>());

        return builder;
    }
}
