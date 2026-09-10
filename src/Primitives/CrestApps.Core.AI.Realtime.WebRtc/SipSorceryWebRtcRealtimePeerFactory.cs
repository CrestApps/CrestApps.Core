using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Realtime.WebRtc;

/// <summary>
/// Creates SIPSorcery-backed server-relay WebRTC peers.
/// </summary>
internal sealed class SipSorceryWebRtcRealtimePeerFactory : IWebRtcRealtimePeerFactory
{
    private readonly ILogger<SipSorceryWebRtcRealtimePeer> _logger;
    private readonly IOptionsMonitor<RealtimeTransportOptions> _options;

    public SipSorceryWebRtcRealtimePeerFactory(
        ILogger<SipSorceryWebRtcRealtimePeer> logger,
        ILoggerFactory loggerFactory,
        IOptionsMonitor<RealtimeTransportOptions> options)
    {
        _logger = logger;
        _options = options;

        // SIPSorcery logs to its own static factory, which defaults to a null logger. Without this, everything it
        // knows about ICE stays invisible: the TURN Allocate exchange, the CreatePermission it installs for each
        // remote candidate, and every connectivity check it sends or answers. Those are the only records of why a
        // relay candidate that was gathered successfully never carries traffic, and no amount of logging on our
        // own side can substitute for them. Raise the "SIPSorcery" category to Debug to read them.
        SIPSorcery.LogFactory.Set(loggerFactory);
    }

    public async Task<IWebRtcRealtimePeer> CreateAsync(string offerSdp, IReadOnlyList<WebRtcIceServer> iceServers, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(offerSdp);

        var peer = new SipSorceryWebRtcRealtimePeer(iceServers ?? [], _logger, _options.CurrentValue.IceTransportPolicy);

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
