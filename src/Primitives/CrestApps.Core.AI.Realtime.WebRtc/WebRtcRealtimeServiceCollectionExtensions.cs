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

        return services;
    }
}
