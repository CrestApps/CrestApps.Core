using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Claude.Handlers;
using CrestApps.Core.AI.Claude.Services;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI.Claude;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Anthropic orchestrator and related services.
    /// </summary>
    public static IServiceCollection AddCoreAIClaudeOrchestrator(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOrchestrator<ClaudeOrchestrator>(ClaudeOrchestrator.OrchestratorName)
            .WithTitle("Claude");

        services.TryAddScoped<ClaudeClientService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionSettingsHandler, ClaudeChatInteractionSettingsHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, ClaudeOrchestrationContextHandler>());

        return services;
    }

    public static CrestAppsAISuiteBuilder AddClaudeOrchestrator(this CrestAppsAISuiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIClaudeOrchestrator();
        return builder;
    }

    [Obsolete("Use AddAISuite(ai => ai.AddClaudeOrchestrator()).")]
    public static CrestAppsCoreBuilder AddClaudeOrchestrator(this CrestAppsCoreBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIClaudeOrchestrator();
        return builder;
    }
}
