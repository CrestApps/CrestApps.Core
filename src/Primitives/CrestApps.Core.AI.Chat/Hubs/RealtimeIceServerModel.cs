using System.Text.Json.Serialization;
using CrestApps.Core.AI.Realtime;

namespace CrestApps.Core.AI.Chat.Hubs;

/// <summary>
/// An ICE (STUN/TURN) server as the browser's <c>RTCPeerConnection</c> expects it. The property names are the
/// ones WebRTC requires verbatim (<c>urls</c>, <c>username</c>, <c>credential</c>), so they are pinned here
/// rather than left to whatever naming policy the hub's JSON protocol happens to use.
/// </summary>
public sealed class RealtimeIceServerModel
{
    /// <summary>
    /// Gets or sets the ICE server URLs.
    /// </summary>
    [JsonPropertyName("urls")]
    public string[] Urls { get; set; } = [];

    /// <summary>
    /// Gets or sets the TURN username. Null for STUN servers.
    /// </summary>
    [JsonPropertyName("username")]
    public string Username { get; set; }

    /// <summary>
    /// Gets or sets the TURN credential. Null for STUN servers.
    /// </summary>
    [JsonPropertyName("credential")]
    public string Credential { get; set; }

    /// <summary>
    /// Creates the browser-facing model for a resolved ICE server.
    /// </summary>
    /// <param name="server">The resolved ICE server.</param>
    public static RealtimeIceServerModel From(WebRtcIceServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        return new RealtimeIceServerModel
        {
            Urls = server.Urls ?? [],
            Username = server.Username,
            Credential = server.Credential,
        };
    }
}
