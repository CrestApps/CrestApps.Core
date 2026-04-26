using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Copilot.Handlers;
using CrestApps.Core.AI.Copilot.Models;
using CrestApps.Core.AI.Copilot.Services;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Copilot;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Copilot orchestrator and related services.
    /// </summary>
    public static IServiceCollection AddCoreAICopilotOrchestrator(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(CopilotOrchestrator.HttpClientName)
            .AddStandardResilienceHandler();

        services.AddOrchestrator<CopilotOrchestrator>(CopilotOrchestrator.OrchestratorName)
            .WithTitle("Copilot");

        services.TryAddScoped<GitHubOAuthService>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionSettingsHandler, CopilotChatInteractionSettingsHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, CopilotOrchestrationContextHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CopilotOptions>, CopilotOptionsValidator>());

        return services;
    }

    public static CrestAppsAISuiteBuilder AddCopilotOrchestrator(this CrestAppsAISuiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAICopilotOrchestrator();
        return builder;
    }
}
