using CrestApps.Core.AI.Realtime;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Resolves the ICE (STUN/TURN) servers offered to the browser for the server-relay WebRTC realtime transport.
/// This is the single source shared by both realtime hubs, and the one place TURN servers and their ephemeral
/// credentials are added when production NAT traversal (Phase 3) is wired.
/// </summary>
internal static class RealtimeWebRtcIceServers
{
    public static IReadOnlyList<WebRtcIceServer> Resolve(IServiceProvider services)
    {
        return
        [
            new WebRtcIceServer { Urls = ["stun:stun.l.google.com:19302"] },
        ];
    }
}
