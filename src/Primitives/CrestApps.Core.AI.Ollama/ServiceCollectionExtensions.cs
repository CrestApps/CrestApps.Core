using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Ollama.Services;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Ollama;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Ollama AI client provider.
    /// </summary>
    public static IServiceCollection AddCoreAIOllama(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIClientProvider, OllamaAIClientProvider>());

        services.AddCoreAIProfile<OllamaCompletionClient>(OllamaConstants.ImplementationName, OllamaConstants.ProviderName, o =>
        {
            o.DisplayName = new LocalizedString("Ollama", "Ollama");
            o.Description = new LocalizedString("Ollama", "Use locally hosted Ollama models for AI completion.");
        });

        services.AddCoreAIConnectionSource(OllamaConstants.ProviderName, o =>
        {
            o.DisplayName = new LocalizedString("Ollama", "Ollama");
            o.Description = new LocalizedString("Ollama", "Use locally hosted Ollama models for AI completion.");
        });

        return services;
    }

    public static CrestAppsAISuiteBuilder AddOllama(this CrestAppsAISuiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIOllama();
        return builder;
    }

    [Obsolete("Use AddAISuite(ai => ai.AddOllama()).")]
    public static CrestAppsCoreBuilder AddOllama(this CrestAppsCoreBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIOllama();
        return builder;
    }

}
