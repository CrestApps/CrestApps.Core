using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI.Azure.Handlers;
using CrestApps.Core.AI.OpenAI.Azure.Models;
using CrestApps.Core.AI.OpenAI.Azure.Services;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.OpenAI.Azure;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Azure OpenAI client provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIAzureOpenAI(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<AzureClientOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<AzureClientOptions>, AzureClientOptionsConfiguration>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIClientProvider, AzureOpenAIClientProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIClientProvider, AzureSpeechClientProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIProviderConnectionHandler, AzureOpenAIConnectionHandler>());

        services.AddCoreAIProfile<AzureOpenAICompletionClient>(AzureOpenAIConstants.ClientName, o =>
        {
            o.DisplayName = new LocalizedString("Azure OpenAI", "Azure OpenAI");
            o.Description = new LocalizedString("Azure OpenAI", "Use Azure OpenAI models for AI completion.");
        });

        services.AddCoreAIConnectionSource(AzureOpenAIConstants.ClientName, o =>
        {
            o.DisplayName = new LocalizedString("Azure OpenAI", "Azure OpenAI");
            o.Description = new LocalizedString("Azure OpenAI", "Use Azure OpenAI models for AI completion.");
        });

        services.AddCoreAIDeploymentProvider(AzureOpenAIConstants.AzureSpeechClientName, o =>
        {
            o.DisplayName = new LocalizedString("Azure AI Services", "Azure AI Services");
            o.Description = new LocalizedString("Azure AI Services", "Use Azure AI Services speech deployments via configuration or the admin UI.");
            o.UseContainedConnection = true;
        });

return services;
    }

    /// <summary>
    /// Adds azure open ai.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsAISuiteBuilder AddAzureOpenAI(this CrestAppsAISuiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIAzureOpenAI();

return builder;
    }
}
