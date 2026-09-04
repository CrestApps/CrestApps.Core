#nullable enable
using System.Collections.Concurrent;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Tracks the running realtime session per SignalR connection so hub calls that arrive on a different invocation
/// than the one driving the session — changing barge-in or turn-detection mid-conversation — can reach it. A
/// singleton, mirroring <see cref="WebRtcRealtimePeerRegistry"/>.
/// </summary>
public sealed class RealtimeSessionRegistry
{
    private readonly ConcurrentDictionary<string, RealtimeSessionControl> _sessions = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers the control for a connection's active session.
    /// </summary>
    /// <param name="connectionId">The SignalR connection id.</param>
    /// <param name="control">The session control.</param>
    public void Add(string connectionId, RealtimeSessionControl control)
    {
        if (!string.IsNullOrEmpty(connectionId) && control is not null)
        {
            _sessions[connectionId] = control;
        }
    }

    /// <summary>
    /// Gets the control for a connection's active session, or <see langword="null"/> when it has none.
    /// </summary>
    /// <param name="connectionId">The SignalR connection id.</param>
    public RealtimeSessionControl? Get(string connectionId)
        => !string.IsNullOrEmpty(connectionId) && _sessions.TryGetValue(connectionId, out var control) ? control : null;

    /// <summary>
    /// Removes a connection's session, if any.
    /// </summary>
    /// <param name="connectionId">The SignalR connection id.</param>
    public void Remove(string connectionId)
    {
        if (!string.IsNullOrEmpty(connectionId))
        {
            _sessions.TryRemove(connectionId, out _);
        }
    }
}
