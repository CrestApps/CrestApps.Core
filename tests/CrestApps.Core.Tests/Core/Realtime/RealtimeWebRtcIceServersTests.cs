using System.Security.Cryptography;
using System.Text;
using CrestApps.Core.AI.Chat.Realtime;
using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Realtime;

public sealed class RealtimeWebRtcIceServersTests
{
    [Fact]
    public void Resolve_WithNoConfiguration_ReturnsDefaultStunOnly()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        var result = RealtimeWebRtcIceServers.Resolve(services);

        var server = Assert.Single(result);
        Assert.Equal(["stun:stun.l.google.com:19302"], server.Urls);
        Assert.Null(server.Username);
        Assert.Null(server.Credential);
    }

    [Fact]
    public void Resolve_WithCustomStun_UsesConfiguredUrls()
    {
        using var services = new ServiceCollection()
            .Configure<RealtimeTransportOptions>(o => o.StunUrls = ["stun:stun.example.com:3478"])
            .BuildServiceProvider();

        var result = RealtimeWebRtcIceServers.Resolve(services);

        Assert.Equal(["stun:stun.example.com:3478"], result[0].Urls);
    }

    [Fact]
    public void Resolve_WithTurnSecret_MintsEphemeralCredential()
    {
        const string secret = "shared-turn-secret";
        using var services = new ServiceCollection()
            .Configure<RealtimeTransportOptions>(o =>
            {
                o.TurnUrls = ["turn:turn.example.com:3478"];
                o.TurnSecret = secret;
                o.TurnCredentialTtlSeconds = 600;
            })
            .BuildServiceProvider();

        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var result = RealtimeWebRtcIceServers.Resolve(services);
        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var turn = result.Single(s => s.Urls.Contains("turn:turn.example.com:3478"));

        // Username is a UNIX expiry timestamp roughly now + ttl.
        var expiry = long.Parse(turn.Username);
        Assert.InRange(expiry, before + 600, after + 600);

        // Credential is Base64(HMAC-SHA1(secret, username)) — the coturn use-auth-secret handshake.
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms — protocol-mandated for TURN REST API.
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));
        var expected = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(turn.Username)));
#pragma warning restore CA5350
        Assert.Equal(expected, turn.Credential);
    }

    [Fact]
    public void Resolve_WithStaticTurnCredentials_UsesThemVerbatim()
    {
        using var services = new ServiceCollection()
            .Configure<RealtimeTransportOptions>(o =>
            {
                o.TurnUrls = ["turn:turn.example.com:3478"];
                o.TurnUsername = "static-user";
                o.TurnCredential = "static-pass";
            })
            .BuildServiceProvider();

        var result = RealtimeWebRtcIceServers.Resolve(services);

        var turn = result.Single(s => s.Username == "static-user");
        Assert.Equal("static-pass", turn.Credential);
    }

    [Fact]
    public void Resolve_WithTurnUrlsButNoCredentials_OmitsTurnServer()
    {
        using var services = new ServiceCollection()
            .Configure<RealtimeTransportOptions>(o => o.TurnUrls = ["turn:turn.example.com:3478"])
            .BuildServiceProvider();

        var result = RealtimeWebRtcIceServers.Resolve(services);

        // Without a secret or static credentials there is nothing to authenticate with, so no TURN entry is added.
        Assert.DoesNotContain(result, s => s.Urls.Contains("turn:turn.example.com:3478"));
    }
}
