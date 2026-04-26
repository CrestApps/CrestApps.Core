using CrestApps.Core.AI.Markdown.Services;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Markdown;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds core ai markdown.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIMarkdown(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAITextNormalizer, MarkdownAITextNormalizer>();

return services;
    }

    /// <summary>
    /// Adds markdown.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsAISuiteBuilder AddMarkdown(this CrestAppsAISuiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIMarkdown();

        return builder;
    }
}
