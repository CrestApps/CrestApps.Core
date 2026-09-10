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

        return services.AddSignalR(options =>
            {
                // Defaults every hub in this host inherits, so a hub that never calls
                // ConfigureCrestAppsChatHubOptions cannot silently run with settings that break realtime
                // signaling. Only the two that are safe for unrelated hubs are set here; the chat hubs'
                // longer timeouts and larger message size stay per hub type.

                // One parallel invocation per client -- SignalR's default -- deadlocks the realtime WebRTC
                // handshake. StartRealtimeWebRtc holds its invocation open while it waits for the peer to
                // connect, so the trickled AddRealtimeIceCandidate calls it is waiting for queue up behind
                // it and ICE can never complete. Loopback hides this completely, because the peer learns the
                // browser's address as a peer-reflexive candidate from the inbound STUN checks; a remote
                // host, whose own candidates are unroutable private addresses, has no such path.
                options.MaximumParallelInvocationsPerClient = 8;

                // The realtime WebSocket transport uploads audio as a client-to-server stream of small
                // frames. The default buffer of ten fills while the provider session is still opening and
                // blocks the connection's dispatch loop.
                options.StreamBufferCapacity = 64;
            })
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
