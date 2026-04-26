using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core;

/// <summary>
/// Extension methods on <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>
/// for registering catalog implementations with the appropriate interface bindings.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds document catalog.
    /// </summary>
    public static IServiceCollection AddDocumentCatalog<TModel, T>(this IServiceCollection services)
         where TModel : CatalogItem
         where T : class, ICatalog<TModel>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<ICatalog<TModel>>();

        services.AddScoped<T>();
        services.AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<T>());

return services;
    }

    /// <summary>
    /// Adds named document catalog.
    /// </summary>
    public static IServiceCollection AddNamedDocumentCatalog<TModel, T>(this IServiceCollection services)
        where TModel : CatalogItem, INameAwareModel
        where T : class, INamedCatalog<TModel>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<INamedCatalog<TModel>>();

        services.AddScoped<T>();
        services.AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<T>());
        services.AddScoped<INamedCatalog<TModel>>(sp => sp.GetRequiredService<T>());

return services;
    }

    /// <summary>
    /// Adds source document catalog.
    /// </summary>
    public static IServiceCollection AddSourceDocumentCatalog<TModel, T>(this IServiceCollection services)
        where TModel : CatalogItem, ISourceAwareModel
        where T : class, ISourceCatalog<TModel>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<ISourceCatalog<TModel>>();

        services.AddScoped<T>();
        services.AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<T>());
        services.AddScoped<ISourceCatalog<TModel>>(sp => sp.GetRequiredService<T>());

return services;
    }

    /// <summary>
    /// Adds named source document catalog.
    /// </summary>
    public static IServiceCollection AddNamedSourceDocumentCatalog<TModel, T>(this IServiceCollection services)
        where TModel : CatalogItem, INameAwareModel, ISourceAwareModel
        where T : class, INamedSourceCatalog<TModel>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<INamedCatalog<TModel>>();
        services.RemoveAll<ISourceCatalog<TModel>>();
        services.RemoveAll<INamedSourceCatalog<TModel>>();

        services.AddScoped<T>();
        services.AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<T>());
        services.AddScoped<INamedCatalog<TModel>>(sp => sp.GetRequiredService<T>());
        services.AddScoped<ISourceCatalog<TModel>>(sp => sp.GetRequiredService<T>());
        services.AddScoped<INamedSourceCatalog<TModel>>(sp => sp.GetRequiredService<T>());

return services;
    }
}
