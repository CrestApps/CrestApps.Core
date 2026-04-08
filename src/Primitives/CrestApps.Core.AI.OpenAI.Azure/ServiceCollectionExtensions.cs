using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.OpenAI.Azure.Services;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.OpenAI.Azure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Azure OpenAI client provider.
    /// </summary>
    public static IServiceCollection AddCoreAIAzureOpenAI(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIClientProvider, AzureOpenAIClientProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIClientProvider, AzureSpeechClientProvider>());

        services.AddCoreAIProfile<AzureOpenAICompletionClient>(AzureOpenAIConstants.ProviderName, AzureOpenAIConstants.ProviderName, o =>
        {
            o.DisplayName = new LocalizedString("Azure OpenAI", "Azure OpenAI");
            o.Description = new LocalizedString("Azure OpenAI", "Use Azure OpenAI models for AI completion.");
        });

        services.AddCoreAIConnectionSource(AzureOpenAIConstants.ProviderName, o =>
        {
            o.DisplayName = new LocalizedString("Azure OpenAI", "Azure OpenAI");
            o.Description = new LocalizedString("Azure OpenAI", "Use Azure OpenAI models for AI completion.");
        });

        services.AddCoreAIDeploymentProvider(AzureOpenAIConstants.AzureSpeechProviderName, o =>
        {
            o.SupportsContainedConnection = true;
            o.DisplayName = new LocalizedString("Azure AI Services", "Azure AI Services");
            o.Description = new LocalizedString("Azure AI Services", "Use Azure AI Services speech deployments via configuration or the admin UI.");
        });

        return services;
    }

    public static CrestAppsAISuiteBuilder AddAzureOpenAI(this CrestAppsAISuiteBuilder builder)
    {
        builder.Services.AddCoreAIAzureOpenAI();
        return builder;
    }

    [Obsolete("Use AddAISuite(ai => ai.AddAzureOpenAI()).")]
    public static CrestAppsCoreBuilder AddAzureOpenAI(this CrestAppsCoreBuilder builder)
    {
        builder.Services.AddCoreAIAzureOpenAI();
        return builder;
    }

}
