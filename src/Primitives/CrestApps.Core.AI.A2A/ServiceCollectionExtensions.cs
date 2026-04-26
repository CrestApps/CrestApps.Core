using CrestApps.Core.AI.A2A.Functions;
using CrestApps.Core.AI.A2A.Handlers;
using CrestApps.Core.AI.A2A.Services;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Builders;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI.A2A;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreAIA2AClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(A2AConstants.HttpClientName)
            .AddStandardResilienceHandler();

        services.AddMemoryCache();
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAICompletionContextBuilderHandler, A2AAICompletionContextBuilderHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IToolRegistryProvider, A2AToolRegistryProvider>());
        services.TryAddSingleton<IA2AAgentCardCacheService, DefaultA2AAgentCardCacheService>();
        services.TryAddScoped<IA2AConnectionAuthService, DefaultA2AConnectionAuthService>();

        services.AddCoreAITool<ListAvailableAgentsFunction>(ListAvailableAgentsFunction.TheName);
        services.AddCoreAITool<FindAgentForTaskFunction>(FindAgentForTaskFunction.TheName);
        services.AddCoreAITool<FindToolsForTaskFunction>(FindToolsForTaskFunction.TheName);

        return services;
    }

    public static CrestAppsAISuiteBuilder AddA2AClient(this CrestAppsAISuiteBuilder builder, Action<CrestAppsA2AClientBuilder> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIA2AClient();

        if (configure is not null)
        {
            configure(new CrestAppsA2AClientBuilder(builder.Services));
        }

        return builder;
    }
}
