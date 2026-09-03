#nullable enable
using System.Collections.Concurrent;
using CrestApps.Core.AI.Realtime;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Tracks the active server-relay WebRTC peer per SignalR connection so trickled ICE candidates — which arrive on
/// a separate hub invocation than the one that created the peer — can be routed to the right peer. A singleton.
/// </summary>
public sealed class WebRtcRealtimePeerRegistry
{
    private readonly ConcurrentDictionary<string, IWebRtcRealtimePeer> _peers = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a connection's peer. A second session on the same connection replaces the first and returns the
    /// one it displaced, so the caller can dispose it: silently overwriting leaked a live peer that kept holding
    /// its sockets and pacing loop for the rest of the connection.
    /// </summary>
    /// <param name="connectionId">The SignalR connection id.</param>
    /// <param name="peer">The peer to register.</param>
    public IWebRtcRealtimePeer? Add(string connectionId, IWebRtcRealtimePeer peer)
    {
        if (string.IsNullOrEmpty(connectionId) || peer is null)
        {
            return null;
        }

        _peers.TryGetValue(connectionId, out var displaced);
        _peers[connectionId] = peer;

        return ReferenceEquals(displaced, peer) ? null : displaced;
    }

    public IWebRtcRealtimePeer? Get(string connectionId)
        => !string.IsNullOrEmpty(connectionId) && _peers.TryGetValue(connectionId, out var peer) ? peer : null;

    public void Remove(string connectionId)
    {
        if (!string.IsNullOrEmpty(connectionId))
        {
            _peers.TryRemove(connectionId, out _);
        }
    }
}
