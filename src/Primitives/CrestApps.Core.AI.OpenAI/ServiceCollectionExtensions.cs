using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.OpenAI.Services;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.OpenAI;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the OpenAI client provider.
    /// </summary>
    public static IServiceCollection AddCoreAIOpenAI(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIClientProvider, OpenAIClientProvider>());

        services.AddCoreAIProfile<OpenAICompletionClient>(OpenAIConstants.ClientName, o =>
        {
            o.DisplayName = new LocalizedString("OpenAI", "OpenAI");
            o.Description = new LocalizedString("OpenAI", "Use OpenAI models for AI completion.");
        });

        services.AddCoreAIConnectionSource(OpenAIConstants.ClientName, o =>
        {
            o.DisplayName = new LocalizedString("OpenAI", "OpenAI");
            o.Description = new LocalizedString("OpenAI", "Use OpenAI models for AI completion.");
        });

        return services;
    }

    public static CrestAppsAISuiteBuilder AddOpenAI(this CrestAppsAISuiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIOpenAI();
        return builder;
    }
}
