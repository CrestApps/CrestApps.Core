using CrestApps.Core.AI.Tooling;
using CrestApps.Core.AI.WebMcp.Models;
using CrestApps.Core.AI.WebMcp.Services;
using CrestApps.Core.Builders;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestApps.Core.AI.WebMcp;

/// <summary>
/// Registers the WebMCP services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services that give AI tools to browser agents: the tool service, the default access evaluator
    /// (every signed-in user), and <see cref="WebMcpOptions"/>. Map the endpoints with
    /// <see cref="Endpoints.WebMcpEndpoints.MapWebMcpTools"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddCoreAIWebMcp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<WebMcpOptions>();
        services.AddAntiforgery();
        services.TryAddScoped<IWebMcpToolService, DefaultWebMcpToolService>();
        services.TryAddSingleton<IWebMcpAccessEvaluator, DefaultWebMcpAccessEvaluator>();

        return services;
    }

    /// <summary>
    /// Registers the services that let the AI chat call the page tools of the browser tab a message is sent from:
    /// the page tool registry, the client that runs tools on <typeparamref name="THub"/> connections with SignalR client
    /// results, and the tool registry provider. Only profiles with <see cref="CrestApps.Core.AI.Models.WebMcpProfileSettings.AllowPageTools"/>
    /// get page tools.
    /// </summary>
    /// <typeparam name="THub">The chat hub the browser tabs connect to.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddCoreAIWebMcpPageTools<THub>(this IServiceCollection services)
        where THub : Hub
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<WebMcpPageToolOptions>();
        services.TryAddSingleton<IWebMcpPageToolRegistry, DefaultWebMcpPageToolRegistry>();
        services.TryAddSingleton<IWebMcpPageToolClient, HubWebMcpPageToolClient<THub>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IToolRegistryProvider, WebMcpPageToolRegistryProvider>());

        return services;
    }

    /// <summary>
    /// Adds the WebMCP services to the AI suite.
    /// </summary>
    /// <param name="builder">The AI suite builder.</param>
    /// <param name="configure">Configures the WebMCP services, for example to add page tools.</param>
    /// <returns>The AI suite builder.</returns>
    public static CrestAppsAISuiteBuilder AddWebMcp(this CrestAppsAISuiteBuilder builder, Action<CrestAppsWebMcpBuilder> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIWebMcp();

        configure?.Invoke(new CrestAppsWebMcpBuilder(builder.Services));

        return builder;
    }

    /// <summary>
    /// Lets the AI chat call the page tools of the browser tab a message is sent from.
    /// </summary>
    /// <typeparam name="THub">The chat hub the browser tabs connect to.</typeparam>
    /// <param name="builder">The WebMCP builder.</param>
    /// <returns>The WebMCP builder.</returns>
    public static CrestAppsWebMcpBuilder AddPageTools<THub>(this CrestAppsWebMcpBuilder builder)
        where THub : Hub
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIWebMcpPageTools<THub>();

        return builder;
    }
}
