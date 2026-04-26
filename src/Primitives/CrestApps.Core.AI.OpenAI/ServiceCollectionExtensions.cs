using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI.Handlers;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.OpenAI;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the OpenAI client provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIOpenAI(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIClientProvider, OpenAI.Services.OpenAIClientProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIProviderConnectionHandler, OpenAIConnectionHandler>());

        services.AddCoreAIProfile<ProviderAICompletionClient<OpenAIClientMarker>>(OpenAIConstants.ClientName, o =>
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

    /// <summary>
    /// Adds open ai.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsAISuiteBuilder AddOpenAI(this CrestAppsAISuiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIOpenAI();

        return builder;
    }
}
