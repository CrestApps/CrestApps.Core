#nullable enable
using System.Collections.Concurrent;
using CrestApps.Core.AI.Realtime;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Tracks the active server-relay WebRTC peer per SignalR connection so trickled ICE candidates — which arrive on
/// a separate hub invocation than the one that created the peer — can be routed to the right peer. A singleton.
/// </summary>
/// <remarks>
/// Candidates that arrive before their peer exists are held rather than dropped. The browser trickles them the
/// moment it has them, which is well before the peer finishes being built: creating the SIPSorcery peer binds a
/// socket, resolves the ICE servers and completes a TURN allocation, then sets the remote description — measured
/// at over five seconds on a cold App Service instance. Discarding that window leaves the peer with no remote
/// candidates at all, so it forms no candidate pairs, sends no connectivity checks, and ICE times out with the
/// browser still in "checking" while both sides hold a perfectly good relay candidate.
/// </remarks>
public sealed class WebRtcRealtimePeerRegistry
{
    // A browser sends on the order of a dozen candidates. The cap only exists so a client that keeps trickling at
    // a connection with no peer cannot grow this without bound.
    private const int MaxPendingCandidates = 64;

    private readonly ConcurrentDictionary<string, IWebRtcRealtimePeer> _peers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Queue<WebRtcIceCandidate>> _pending = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a connection's peer and hands it any candidates that arrived while it was being built. A second
    /// session on the same connection replaces the first and returns the one it displaced, so the caller can
    /// dispose it: silently overwriting leaked a live peer that kept holding its sockets and pacing loop for the
    /// rest of the connection.
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

        FlushPending(connectionId);

        return ReferenceEquals(displaced, peer) ? null : displaced;
    }

    /// <summary>
    /// Routes a trickled ICE candidate to the connection's peer, holding it until the peer exists when it does
    /// not yet. Candidates are delivered in the order they arrived.
    /// </summary>
    /// <param name="connectionId">The SignalR connection id.</param>
    /// <param name="candidate">The candidate to deliver.</param>
    public void AddIceCandidate(string connectionId, WebRtcIceCandidate candidate)
    {
        if (string.IsNullOrEmpty(connectionId) || candidate is null)
        {
            return;
        }

        var queue = _pending.GetOrAdd(connectionId, static _ => new Queue<WebRtcIceCandidate>());

        // Queueing and draining share the lock so a candidate cannot overtake one already waiting — the browser
        // signals the end of gathering with an empty candidate, which must not arrive before the real ones.
        lock (queue)
        {
            if (_peers.TryGetValue(connectionId, out var peer) && queue.Count == 0)
            {
                peer.AddIceCandidate(candidate);

                return;
            }

            if (queue.Count < MaxPendingCandidates)
            {
                queue.Enqueue(candidate);
            }
        }

        // The peer may have registered between the two, in which case nothing else is coming to drain the queue.
        FlushPending(connectionId);
    }

    public IWebRtcRealtimePeer? Get(string connectionId)
        => !string.IsNullOrEmpty(connectionId) && _peers.TryGetValue(connectionId, out var peer) ? peer : null;

    public void Remove(string connectionId)
    {
        if (!string.IsNullOrEmpty(connectionId))
        {
            _peers.TryRemove(connectionId, out _);
            _pending.TryRemove(connectionId, out _);
        }
    }

    private void FlushPending(string connectionId)
    {
        if (!_pending.TryGetValue(connectionId, out var queue))
        {
            return;
        }

        lock (queue)
        {
            if (!_peers.TryGetValue(connectionId, out var peer))
            {
                return;
            }

            while (queue.Count > 0)
            {
                peer.AddIceCandidate(queue.Dequeue());
            }
        }
    }
}
