using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentCatalog<TModel, T>(this IServiceCollection services)
         where TModel : CatalogItem
         where T : class, ICatalog<TModel>
    {
        services.RemoveAll<ICatalog<TModel>>();

        services.AddScoped<T>();
        services.AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<T>());

        return services;
    }

    public static IServiceCollection AddNamedDocumentCatalog<TModel, T>(this IServiceCollection services)
        where TModel : CatalogItem, INameAwareModel
        where T : class, INamedCatalog<TModel>
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<INamedCatalog<TModel>>();

        services.AddScoped<T>();
        services.AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<T>());
        services.AddScoped<INamedCatalog<TModel>>(sp => sp.GetRequiredService<T>());

        return services;
    }

    public static IServiceCollection AddSourceDocumentCatalog<TModel, T>(this IServiceCollection services)
        where TModel : CatalogItem, ISourceAwareModel
        where T : class, ISourceCatalog<TModel>
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<ISourceCatalog<TModel>>();

        services.AddScoped<T>();
        services.AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<T>());
        services.AddScoped<ISourceCatalog<TModel>>(sp => sp.GetRequiredService<T>());

        return services;
    }

    public static IServiceCollection AddNamedSourceDocumentCatalog<TModel, T>(this IServiceCollection services)
        where TModel : CatalogItem, INameAwareModel, ISourceAwareModel
        where T : class, INamedSourceCatalog<TModel>
    {
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
