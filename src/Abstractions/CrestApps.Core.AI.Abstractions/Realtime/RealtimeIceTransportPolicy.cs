namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// Which ICE candidates the server offers when negotiating a realtime WebRTC session. Mirrors the
/// <c>RTCIceTransportPolicy</c> of the WebRTC specification.
/// </summary>
public enum RealtimeIceTransportPolicy
{
    /// <summary>
    /// Offer every candidate the server gathers: host, server-reflexive and relay. The default, and the fastest
    /// and cheapest route whenever the two peers can reach each other directly.
    /// </summary>
    All,

    /// <summary>
    /// Offer only relay candidates, so all media travels through the configured TURN server.
    /// </summary>
    /// <remarks>
    /// Two reasons to choose this. It keeps the server's host and server-reflexive addresses out of the candidate
    /// list every browser receives, which would otherwise disclose internal addressing to anyone who starts a
    /// voice session. And it makes the relay path the only path, so a developer machine behaves like a deployment
    /// whose inbound UDP is blocked, where a direct pair is impossible — without it, a local peer can connect
    /// directly and a broken relay path looks healthy. It costs a TURN allocation for every session.
    /// </remarks>
    Relay,
}
