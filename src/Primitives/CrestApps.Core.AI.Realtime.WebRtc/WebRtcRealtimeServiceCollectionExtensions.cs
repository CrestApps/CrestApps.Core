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
    public static IServiceCollection AddWebRtcRealtimeTransport(this IServiceCollection services)
    {
        services.TryAddSingleton<IWebRtcRealtimePeerFactory, SipSorceryWebRtcRealtimePeerFactory>();

        // Bind the ICE (STUN/TURN) server configuration used when the server offers WebRTC to the browser.
        services.AddOptions<RealtimeTransportOptions>().BindConfiguration("CrestApps:AI:RealtimeTransport");

        return services;
    }
}
