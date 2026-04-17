using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI.Indexing;

public static class ServiceCollectionExtensions
{
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
