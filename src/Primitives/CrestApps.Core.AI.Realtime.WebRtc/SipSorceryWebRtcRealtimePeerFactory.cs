using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Realtime.WebRtc;

/// <summary>
/// Creates SIPSorcery-backed server-relay WebRTC peers.
/// </summary>
internal sealed class SipSorceryWebRtcRealtimePeerFactory : IWebRtcRealtimePeerFactory
{
    private readonly ILogger<SipSorceryWebRtcRealtimePeer> _logger;

    public SipSorceryWebRtcRealtimePeerFactory(ILogger<SipSorceryWebRtcRealtimePeer> logger)
    {
        _logger = logger;
    }

    public async Task<IWebRtcRealtimePeer> CreateAsync(string offerSdp, IReadOnlyList<WebRtcIceServer> iceServers, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(offerSdp);

        var peer = new SipSorceryWebRtcRealtimePeer(iceServers ?? [], _logger);

        try
        {
            await peer.InitializeAsync(offerSdp);
        }
        catch
        {
            await peer.DisposeAsync();

            throw;
        }

        return peer;
    }
}
