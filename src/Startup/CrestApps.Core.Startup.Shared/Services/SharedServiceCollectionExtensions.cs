using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Templates.Providers;
using CrestApps.Core.Templates.Services;
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
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ArticleIndexingService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIndexProfileHandler, ArticleIndexProfileHandler>());

        return services;
    }

    /// <summary>
    /// Registers shared template providers that surface host-managed system prompt templates through <see cref="ITemplateService"/>.
    /// </summary>
    public static IServiceCollection AddSharedTemplateProviders(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITemplateProvider, AIProfileSystemPromptTemplateProvider>());

        return services;
    }
}
