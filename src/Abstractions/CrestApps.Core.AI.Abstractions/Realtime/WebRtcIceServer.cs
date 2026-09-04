namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// A STUN/TURN server the WebRTC peer uses for ICE (NAT traversal). TURN entries also carry short-lived
/// credentials so the browser can relay when direct connectivity is blocked.
/// </summary>
public sealed class WebRtcIceServer
{
    /// <summary>
    /// Gets or sets the ICE server URLs (for example <c>stun:stun.l.google.com:19302</c> or
    /// <c>turn:turn.example.com:3478</c>).
    /// </summary>
    public string[] Urls { get; set; } = [];

    /// <summary>
    /// Gets or sets the (typically ephemeral) TURN username. Null for STUN.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    /// Gets or sets the (typically ephemeral) TURN credential. Null for STUN.
    /// </summary>
    public string Credential { get; set; }
}
