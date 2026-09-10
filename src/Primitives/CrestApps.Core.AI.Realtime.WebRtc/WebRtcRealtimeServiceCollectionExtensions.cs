using CrestApps.Core.AI.Realtime;
using CrestApps.Core.AI.Realtime.WebRtc;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration for the server-relay WebRTC realtime transport.
/// </summary>
public static class WebRtcRealtimeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SIPSorcery-backed <see cref="IWebRtcRealtimePeerFactory"/>. When present, the realtime hubs
    /// offer WebRTC as the primary transport; otherwise they use the WebSocket transport.
    /// </summary>
    /// <remarks>
    /// Safe to call more than once. Hosts that enable several realtime surfaces call this per feature, and the
    /// configuration binding is not naturally idempotent: <c>BindConfiguration</c> adds another
    /// <c>IConfigureOptions</c> each time, and the binder *appends* to array properties rather than replacing
    /// them. A second call therefore used to duplicate every configured STUN and TURN URL, and the browser was
    /// offered each one twice — which makes the peer allocate two TURN relays per session, billed twice, and
    /// litters the console with duplicate ICE candidate errors.
    /// </remarks>
    public static IServiceCollection AddWebRtcRealtimeTransport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(IWebRtcRealtimePeerFactory)))
        {
            return services;
        }

        services.TryAddSingleton<IWebRtcRealtimePeerFactory, SipSorceryWebRtcRealtimePeerFactory>();

        // Bind the ICE (STUN/TURN) server configuration used when the server offers WebRTC to the browser.
        services.AddOptions<RealtimeTransportOptions>().BindConfiguration("CrestApps:AI:RealtimeTransport");

        return services;
    }
}
