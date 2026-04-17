using CrestApps.Core.AI.Markdown.Services;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Markdown;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreAIMarkdown(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAITextNormalizer, MarkdownAITextNormalizer>();

        return services;
    }

    public static CrestAppsAISuiteBuilder AddMarkdown(this CrestAppsAISuiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIMarkdown();
        return builder;
    }
}
