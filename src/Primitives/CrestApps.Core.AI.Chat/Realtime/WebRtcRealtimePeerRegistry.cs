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

    public void Add(string connectionId, IWebRtcRealtimePeer peer)
    {
        if (!string.IsNullOrEmpty(connectionId) && peer is not null)
        {
            _peers[connectionId] = peer;
        }
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
