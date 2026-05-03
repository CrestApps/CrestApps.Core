using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Ollama.Services;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Ollama;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Ollama AI client provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIOllama(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIClientProvider, OllamaAIClientProvider>());

        services.AddCoreAIProfile<ProviderAICompletionClient<OllamaClientMarker>>(OllamaConstants.ClientName, o =>
        {
            o.DisplayName = new LocalizedString("Ollama", "Ollama");
            o.Description = new LocalizedString("Ollama", "Use locally hosted Ollama models for AI completion.");
        });

        services.AddCoreAIConnectionSource(OllamaConstants.ClientName, o =>
        {
            o.DisplayName = new LocalizedString("Ollama", "Ollama");
            o.Description = new LocalizedString("Ollama", "Use locally hosted Ollama models for AI completion.");
        });

        services.AddCoreAIDeploymentProvider(OllamaConstants.ClientName, o =>
        {
            o.DisplayName = new LocalizedString("Ollama", "Ollama");
            o.Description = new LocalizedString("Ollama", "Use locally hosted Ollama models for AI deployments.");
        });

        return services;
    }

    /// <summary>
    /// Adds ollama.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsAISuiteBuilder AddOllama(this CrestAppsAISuiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIOllama();

        return builder;
    }
}
