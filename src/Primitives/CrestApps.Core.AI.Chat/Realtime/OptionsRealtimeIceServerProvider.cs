using CrestApps.Core.AI.Realtime;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Resolves the realtime ICE servers from <see cref="RealtimeTransportOptions"/>, which is what a deployment
/// running its own STUN and TURN servers configures.
/// </summary>
/// <remarks>
/// This is the default provider and needs no I/O: the servers are named in configuration, and a coturn shared
/// secret is signed locally per call. A deployment on a hosted TURN service that mints credentials through an
/// HTTP API replaces it — see <see cref="CloudflareRealtimeIceServerProvider"/>.
/// </remarks>
internal sealed class OptionsRealtimeIceServerProvider : IRealtimeIceServerProvider
{
    private readonly IServiceProvider _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="OptionsRealtimeIceServerProvider"/> class.
    /// </summary>
    /// <param name="services">The service provider used to read the transport options.</param>
    public OptionsRealtimeIceServerProvider(IServiceProvider services)
    {
        _services = services;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<WebRtcIceServer>> GetIceServersAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(RealtimeWebRtcIceServers.Resolve(_services));
}
