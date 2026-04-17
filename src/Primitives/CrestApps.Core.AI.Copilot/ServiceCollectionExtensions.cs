using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Copilot.Handlers;
using CrestApps.Core.AI.Copilot.Services;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI.Copilot;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Copilot orchestrator and related services.
    /// </summary>
    public static IServiceCollection AddCoreAICopilotOrchestrator(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient();

        services.AddOrchestrator<CopilotOrchestrator>(CopilotOrchestrator.OrchestratorName)
            .WithTitle("Copilot");

        services.TryAddScoped<GitHubOAuthService>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionSettingsHandler, CopilotChatInteractionSettingsHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, CopilotOrchestrationContextHandler>());

        return services;
    }

    public static CrestAppsAISuiteBuilder AddCopilotOrchestrator(this CrestAppsAISuiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAICopilotOrchestrator();
        return builder;
    }

    [Obsolete("Use AddAISuite(ai => ai.AddCopilotOrchestrator()).")]
    public static CrestAppsCoreBuilder AddCopilotOrchestrator(this CrestAppsCoreBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAICopilotOrchestrator();
        return builder;
    }

}
