using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.Startup.Shared.Services;

public static class SharedServiceCollectionExtensions
{
    /// <summary>
    /// Registers shared article-related services (indexing service, index profile handler).
    /// </summary>
    public static IServiceCollection AddSharedArticleServices(this IServiceCollection services)
    {
        services.AddScoped<ArticleIndexingService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIndexProfileHandler, ArticleIndexProfileHandler>());

        return services;
    }
}
