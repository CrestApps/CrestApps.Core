using CrestApps.Core.Builders;
using CrestApps.Core.SignalR.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.SignalR;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds CrestApps SignalR services including hub route management.
    /// </summary>
    public static ISignalRServerBuilder AddCoreSignalR(this IServiceCollection services, string pathPrefix = "")
    {
        services.AddSingleton(new HubRouteManager(pathPrefix));
        return services.AddSignalR()
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            });
    }

    public static CrestAppsAISuiteBuilder AddSignalR(this CrestAppsAISuiteBuilder builder, string pathPrefix = "", bool addStoreCommitterFilter = false)
    {
        var signalRBuilder = builder.Services.AddCoreSignalR(pathPrefix);
        if (addStoreCommitterFilter)
        {
            signalRBuilder.AddCrestAppsStoreCommitterFilter();
        }

        return builder;
    }

    [Obsolete("Use AddAISuite(ai => ai.AddSignalR(...)).")]
    public static CrestAppsCoreBuilder AddSignalR(this CrestAppsCoreBuilder builder, string pathPrefix = "", bool addStoreCommitterFilter = false)
    {
        var signalRBuilder = builder.Services.AddCoreSignalR(pathPrefix);
        if (addStoreCommitterFilter)
        {
            signalRBuilder.AddCrestAppsStoreCommitterFilter();
        }

        return builder;
    }
}
