using System.Text.Json;
using CrestApps.Core.Builders;
using CrestApps.Core.SignalR.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.SignalR;

/// <summary>
/// Provides extension methods for service Collection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds CrestApps SignalR services including hub route management.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="pathPrefix">The path prefix.</param>
    public static ISignalRServerBuilder AddCoreSignalR(this IServiceCollection services, string pathPrefix = "")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(pathPrefix);

        services.AddSingleton(new HubRouteManager(pathPrefix));

        return services.AddSignalR()
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });
    }

    /// <summary>
    /// Adds signal r.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="pathPrefix">The path prefix.</param>
    /// <param name="addStoreCommitterFilter">The add store committer filter.</param>
    public static CrestAppsAISuiteBuilder AddSignalR(this CrestAppsAISuiteBuilder builder, string pathPrefix = "", bool addStoreCommitterFilter = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(pathPrefix);

        var signalRBuilder = builder.Services.AddCoreSignalR(pathPrefix);
        if (addStoreCommitterFilter)
        {
            signalRBuilder.AddCrestAppsStoreCommitterFilter();
        }

        return builder;
    }
}
