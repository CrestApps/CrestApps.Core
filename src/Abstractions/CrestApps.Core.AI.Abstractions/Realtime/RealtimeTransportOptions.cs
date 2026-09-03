namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// Configuration for the realtime WebRTC transport's ICE (NAT traversal) servers. Bound from configuration
/// (section <c>CrestApps:AI:RealtimeTransport</c>) and consumed when the server builds the ICE server list it
/// offers the browser. STUN alone lets peers connect directly through most home/office NATs; a TURN server is
/// required for users behind strict/symmetric NATs or blocked UDP, where traffic must be relayed.
/// </summary>
public sealed class RealtimeTransportOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the server-relay WebRTC transport is offered to browsers. Defaults
    /// to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// WebRTC needs inbound UDP (or a TURN relay the server can reach). Hosts that cannot provide either — most
    /// PaaS environments, for example — should turn this off: otherwise every voice session spends its connection
    /// timeout attempting a peer that can never connect before falling back to the WebSocket transport, which is
    /// pure added latency at the start of every conversation.
    /// </remarks>
    public bool EnableWebRtc { get; set; } = true;

    /// <summary>
    /// Gets or sets how long a realtime session may go without the user speaking before it is ended, in minutes.
    /// Zero or negative disables the timeout. Defaults to 10 minutes.
    /// </summary>
    /// <remarks>
    /// A realtime session bills for an open provider connection whether or not anyone is talking, and a forgotten
    /// browser tab will happily hold one until the provider's own session cap (typically an hour) closes it. The
    /// client is told why the session ended and can offer to resume.
    /// </remarks>
    public int IdleTimeoutMinutes { get; set; } = 10;

    /// <summary>
    /// Gets or sets the STUN server URLs (for example <c>stun:stun.l.google.com:19302</c>). When empty, a public
    /// default STUN server is used so direct connectivity still works out of the box.
    /// </summary>
    public string[] StunUrls { get; set; } = [];

    /// <summary>
    /// Gets or sets the TURN server URLs (for example <c>turn:turn.example.com:3478</c> or
    /// <c>turns:turn.example.com:5349</c>). When empty, no TURN relay is offered and users behind strict NATs
    /// fall back to the WebSocket transport.
    /// </summary>
    public string[] TurnUrls { get; set; } = [];

    /// <summary>
    /// Gets or sets the shared secret for coturn's <c>use-auth-secret</c> (REST API) mode. When set, the server
    /// mints short-lived ephemeral TURN credentials per session (username = expiry timestamp, credential =
    /// Base64(HMAC-SHA1(secret, username))) so a long-lived TURN password never reaches the browser. Preferred
    /// over <see cref="TurnUsername"/>/<see cref="TurnCredential"/> for production.
    /// </summary>
    public string TurnSecret { get; set; }

    /// <summary>
    /// Gets or sets the lifetime, in seconds, of a minted ephemeral TURN credential. Defaults to one hour.
    /// Ignored unless <see cref="TurnSecret"/> is set.
    /// </summary>
    public int TurnCredentialTtlSeconds { get; set; } = 3600;

    /// <summary>
    /// Gets or sets a static TURN username. Used only when <see cref="TurnSecret"/> is not set. Static credentials
    /// are simpler but long-lived; prefer the ephemeral secret in production.
    /// </summary>
    public string TurnUsername { get; set; }

    /// <summary>
    /// Gets or sets a static TURN credential (password). Used only when <see cref="TurnSecret"/> is not set.
    /// </summary>
    public string TurnCredential { get; set; }
}
