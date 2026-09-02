namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// Creates server-relay <see cref="IWebRtcRealtimePeer"/> instances from a browser's SDP offer. Registered only
/// when a WebRTC transport implementation is available; the hubs treat its absence as "WebRTC not supported" and
/// fall back to the WebSocket transport.
/// </summary>
public interface IWebRtcRealtimePeerFactory
{
    /// <summary>
    /// Creates a peer for the given SDP offer and ICE servers, sets the remote description, and produces the SDP
    /// answer (exposed on <see cref="IWebRtcRealtimePeer.AnswerSdp"/>).
    /// </summary>
    /// <param name="offerSdp">The browser's SDP offer.</param>
    /// <param name="iceServers">The STUN/TURN servers to use for ICE.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<IWebRtcRealtimePeer> CreateAsync(string offerSdp, IReadOnlyList<WebRtcIceServer> iceServers, CancellationToken cancellationToken);
}
