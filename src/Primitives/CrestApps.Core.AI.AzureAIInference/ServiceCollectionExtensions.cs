using CrestApps.Core.AI.AzureAIInference.Services;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.AzureAIInference;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Azure AI Inference (GitHub Models) client provider.
    /// </summary>
    public static IServiceCollection AddCoreAIAzureAIInference(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIClientProvider, AzureAIInferenceClientProvider>());

        services.AddCoreAIProfile<ProviderAICompletionClient<AzureAIInferenceClientMarker>>(AzureAIInferenceConstants.ClientName, o =>
        {
            o.DisplayName = new LocalizedString("Azure AI Inference", "Azure AI Inference / GitHub Models");
            o.Description = new LocalizedString("Azure AI Inference", "Use Azure AI Inference or GitHub Models for AI completion.");
        });

        services.AddCoreAIConnectionSource(AzureAIInferenceConstants.ClientName, o =>
        {
            o.DisplayName = new LocalizedString("Azure AI Inference", "Azure AI Inference / GitHub Models");
            o.Description = new LocalizedString("Azure AI Inference", "Use Azure AI Inference or GitHub Models for AI completion.");
        });

        return services;
    }

    public static CrestAppsAISuiteBuilder AddAzureAIInference(this CrestAppsAISuiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIAzureAIInference();
        return builder;
    }
}
