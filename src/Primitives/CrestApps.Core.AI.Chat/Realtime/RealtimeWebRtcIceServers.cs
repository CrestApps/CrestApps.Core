using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Resolves the ICE (STUN/TURN) servers offered to the browser for the server-relay WebRTC realtime transport.
/// This is the single source shared by both realtime hubs. STUN enables direct connectivity through most NATs;
/// when a TURN server is configured (see <see cref="RealtimeTransportOptions"/>), TURN servers are added — with
/// short-lived ephemeral credentials minted per call when a coturn shared secret is configured — so users behind
/// strict/symmetric NATs or blocked UDP can relay instead of dropping to the WebSocket fallback.
/// </summary>
internal static class RealtimeWebRtcIceServers
{
    private const string DefaultStunUrl = "stun:stun.l.google.com:19302";

    public static IReadOnlyList<WebRtcIceServer> Resolve(IServiceProvider services)
    {
        var options = services.GetService<IOptions<RealtimeTransportOptions>>()?.Value ?? new RealtimeTransportOptions();

        var servers = new List<WebRtcIceServer>
        {
            new()
            {
                Urls = (options.StunUrls is { Length: > 0 }) ? options.StunUrls : [DefaultStunUrl],
            },
        };

        if (options.TurnUrls is { Length: > 0 } turnUrls)
        {
            if (!string.IsNullOrWhiteSpace(options.TurnSecret))
            {
                servers.Add(CreateEphemeralTurnServer(turnUrls, options.TurnSecret, options.TurnCredentialTtlSeconds));
            }
            else if (!string.IsNullOrWhiteSpace(options.TurnUsername))
            {
                servers.Add(new WebRtcIceServer
                {
                    Urls = turnUrls,
                    Username = options.TurnUsername,
                    Credential = options.TurnCredential,
                });
            }
        }

        return servers;
    }

    // coturn "use-auth-secret" (TURN REST API) ephemeral credentials: the username is a UNIX expiry timestamp and
    // the credential is Base64(HMAC-SHA1(secret, username)). coturn is configured with the same shared secret and
    // validates the pair itself, so no long-lived TURN password is ever sent to the browser. HMAC-SHA1 is not a
    // security choice here — it is the algorithm the TURN REST API protocol mandates for this handshake.
    private static WebRtcIceServer CreateEphemeralTurnServer(string[] turnUrls, string secret, int ttlSeconds)
    {
        var ttl = ttlSeconds > 0 ? ttlSeconds : 3600;
        var expiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttl;
        var username = expiry.ToString(CultureInfo.InvariantCulture);

        // HMAC-SHA1 is mandated by the TURN REST API (coturn use-auth-secret) protocol; coturn computes and
        // validates the credential with the same algorithm, so it is a wire-format requirement, not a security
        // choice we are free to change. It authenticates a short-lived TURN grant, not sensitive data at rest.
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));
        var credential = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));
#pragma warning restore CA5350

        return new WebRtcIceServer
        {
            Urls = turnUrls,
            Username = username,
            Credential = credential,
        };
    }
}
