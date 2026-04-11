using CrestApps.Core.Builders;
using CrestApps.Core.Data.YesSql.Indexes;
using CrestApps.Core.Data.YesSql.Services;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using YesSql;
using ISession = YesSql.ISession;

namespace CrestApps.Core.Data.YesSql;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreYesSqlDataStore(this IServiceCollection services, Func<Configuration, IConfiguration> configure)
    {
        services.AddSingleton<IStore>(_ => StoreFactory.CreateAndInitializeAsync(configure(new Configuration())).GetAwaiter().GetResult());

        services.AddScoped(sp => sp.GetRequiredService<IStore>().CreateSession());
        services.AddScoped<IStoreCommitter, YesSqlStoreCommitter>();
        services.AddCatalogManagers();

        return services;
    }

    public static CrestAppsCoreBuilder AddYesSqlDataStore(this CrestAppsCoreBuilder builder, Func<Configuration, IConfiguration> configure)
    {
        builder.Services.AddCoreYesSqlDataStore(configure);
        return builder;
    }

    public static IServiceCollection AddYesSqlDocumentCatalogs(this IServiceCollection services)
    {
        services.TryAddScoped(typeof(ICatalog<>), typeof(DocumentCatalog<,>));
        services.TryAddScoped(typeof(INamedCatalog<>), typeof(NamedDocumentCatalog<,>));
        services.TryAddScoped(typeof(ISourceCatalog<>), typeof(SourceDocumentCatalog<,>));
        services.TryAddScoped(typeof(INamedSourceCatalog<>), typeof(NamedSourceDocumentCatalog<,>));

        return services;
    }

    public static IServiceCollection AddYesSqlDocumentCatalog<TModel, TIndex>(this IServiceCollection services, string collection = null)
        where TModel : CatalogItem
        where TIndex : CatalogItemIndex
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.AddScoped<ICatalog<TModel>>(sp =>
        {
            var session = sp.GetRequiredService<ISession>();

            return new DocumentCatalog<TModel, TIndex>(session, collection);
        });

        return services;
    }

    public static IServiceCollection AddYesSqlNamedDocumentCatalog<TModel, TIndex>(this IServiceCollection services, string collection = null)
        where TModel : CatalogItem, INameAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<INamedCatalog<TModel>>();

        services.AddScoped<ICatalog<TModel>>(sp =>
        {
            var session = sp.GetRequiredService<ISession>();

            return new NamedDocumentCatalog<TModel, TIndex>(session, collection);
        });

        services.AddScoped<INamedCatalog<TModel>>(sp => (INamedCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());

        return services;
    }

    public static IServiceCollection AddYesSqlSourceDocumentCatalog<TModel, TIndex>(this IServiceCollection services, string collection = null)
        where TModel : CatalogItem, ISourceAwareModel
        where TIndex : CatalogItemIndex, ISourceAwareIndex
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<ISourceCatalog<TModel>>();

        services.AddScoped<ICatalog<TModel>>(sp =>
        {
            var session = sp.GetRequiredService<ISession>();

            return new SourceDocumentCatalog<TModel, TIndex>(session, collection);
        });

        services.AddScoped<ISourceCatalog<TModel>>(sp => (ISourceCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());

        return services;
    }

    public static IServiceCollection AddYesSqlNamedSourceDocumentCatalog<TModel, TIndex>(this IServiceCollection services, string collection = null)
        where TModel : CatalogItem, INameAwareModel, ISourceAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
    {
        services.RemoveAll<ICatalog<TModel>>();
        services.RemoveAll<INamedCatalog<TModel>>();
        services.RemoveAll<ISourceCatalog<TModel>>();
        services.RemoveAll<INamedSourceCatalog<TModel>>();

        services.AddScoped<ICatalog<TModel>>(sp =>
        {
            var session = sp.GetRequiredService<ISession>();

            return new NamedSourceDocumentCatalog<TModel, TIndex>(session, collection);
        });

        services.AddScoped<INamedCatalog<TModel>>(sp => (INamedCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());
        services.AddScoped<ISourceCatalog<TModel>>(sp => (ISourceCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());
        services.AddScoped<INamedSourceCatalog<TModel>>(sp => (INamedSourceCatalog<TModel>)sp.GetRequiredService<ICatalog<TModel>>());

        return services;
    }

    public static IServiceCollection AddYesSqlNamedDocumentCatalog<TModel, TIndex, TService>(this IServiceCollection services)
        where TModel : CatalogItem, INameAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex
        where TService : class, ICatalog<TModel>
    {
        services
            .RemoveAll<ICatalog<TModel>>();

        services.TryAddScoped<TService>();

        services
            .AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<TService>());

        return services;
    }

    public static IServiceCollection AddYesSqlDocumentCatalog<TModel, TIndex, TService>(this IServiceCollection services)
        where TModel : CatalogItem
        where TIndex : CatalogItemIndex
        where TService : class, ICatalog<TModel>
    {
        services
            .RemoveAll<ICatalog<TModel>>();

        services.TryAddScoped<TService>();

        services.AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<TService>());

        return services;
    }

    public static IServiceCollection AddYesSqlSourceDocumentCatalog<TModel, TIndex, TService>(this IServiceCollection services)
        where TModel : CatalogItem, ISourceAwareModel
        where TIndex : CatalogItemIndex, ISourceAwareIndex
        where TService : class, ISourceCatalog<TModel>
    {
        services
            .RemoveAll<ICatalog<TModel>>()
            .RemoveAll<ISourceCatalog<TModel>>();

        services.TryAddScoped<TService>();

        services
            .AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<TService>())
            .AddScoped<ISourceCatalog<TModel>>(sp => sp.GetRequiredService<TService>());

        return services;
    }

    public static IServiceCollection AddYesSqlNamedSourceDocumentCatalog<TModel, TIndex, TService>(this IServiceCollection services)
        where TModel : CatalogItem, INameAwareModel, ISourceAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
        where TService : class, INamedSourceCatalog<TModel>
    {
        services
            .RemoveAll<ICatalog<TModel>>()
            .RemoveAll<INamedCatalog<TModel>>()
            .RemoveAll<ISourceCatalog<TModel>>()
            .RemoveAll<INamedSourceCatalog<TModel>>();

        services.TryAddScoped<TService>();

        services
            .AddScoped<ICatalog<TModel>>(sp => sp.GetRequiredService<TService>())
            .AddScoped<INamedCatalog<TModel>>(sp => sp.GetRequiredService<TService>())
            .AddScoped<ISourceCatalog<TModel>>(sp => sp.GetRequiredService<TService>())
            .AddScoped<INamedSourceCatalog<TModel>>(sp => sp.GetRequiredService<TService>());

        return services;
    }

    /// <summary>
    /// Registers a YesSql-backed <see cref="NamedSourceDocumentCatalog{TModel,TIndex}"/>
    /// as an <see cref="INamedSourceCatalogSource{TModel}"/> binding source for the
    /// multi-source store pattern.
    /// </summary>
    public static IServiceCollection AddYesSqlNamedSourceBindingSource<TModel, TIndex>(this IServiceCollection services, string collection = null)
        where TModel : CatalogItem, INameAwareModel, ISourceAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
    {
        services.AddScoped(sp => new NamedSourceDocumentCatalog<TModel, TIndex>(sp.GetRequiredService<ISession>(), collection));
        services.AddScoped<INamedSourceCatalogSource<TModel>>(sp =>
            new WritableCatalogBindingSource<TModel>(sp.GetRequiredService<NamedSourceDocumentCatalog<TModel, TIndex>>()));

        return services;
    }

    /// <summary>
    /// Registers a YesSql-backed <see cref="NamedDocumentCatalog{TModel,TIndex}"/>
    /// as an <see cref="INamedCatalogSource{TModel}"/> binding source for the
    /// multi-source store pattern.
    /// </summary>
    public static IServiceCollection AddYesSqlNamedBindingSource<TModel, TIndex>(this IServiceCollection services)
        where TModel : CatalogItem, INameAwareModel
        where TIndex : CatalogItemIndex, INameAwareIndex
    {
        services.AddScoped<NamedDocumentCatalog<TModel, TIndex>>();
        services.AddScoped<INamedCatalogSource<TModel>>(sp =>
            new WritableNamedCatalogBindingSource<TModel>(sp.GetRequiredService<NamedDocumentCatalog<TModel, TIndex>>()));

        return services;
    }
}
