using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Claude.Handlers;
using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.AI.Claude.Services;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Claude;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Anthropic orchestrator and related services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIClaudeOrchestrator(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOrchestrator<ClaudeOrchestrator>(ClaudeOrchestrator.OrchestratorName)
            .WithTitle("Claude");

        services.TryAddScoped<ClaudeClientService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionSettingsHandler, ClaudeChatInteractionSettingsHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, ClaudeOrchestrationContextHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ClaudeOptions>, ClaudeOptionsValidator>());

        return services;
    }

    /// <summary>
    /// Adds claude orchestrator.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsAISuiteBuilder AddClaudeOrchestrator(this CrestAppsAISuiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIClaudeOrchestrator();

        return builder;
    }
}
