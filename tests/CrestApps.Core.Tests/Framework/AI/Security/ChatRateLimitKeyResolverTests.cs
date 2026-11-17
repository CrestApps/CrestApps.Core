using System.Security.Claims;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.Tests.Framework.AI.Security;

public sealed class ChatRateLimitKeyResolverTests
{
    // ---- ResolveMessageKeys ----

    [Fact]
    public void ResolveMessageKeys_AuthenticatedWithoutNetworkAddress_ReturnsUserKeyOnly()
    {
        var context = CreateContext(user: UserWithNameIdentifier("u1"));

        var keys = ChatRateLimitKeyResolver.ResolveMessageKeys(context, new AIChatRateLimitingOptions());

        Assert.Equal(["user:u1"], keys);
    }

    [Fact]
    public void ResolveMessageKeys_AuthenticatedWithIp_ReturnsUserAndIpKeys()
    {
        var context = CreateContext(user: UserWithNameIdentifier("u1"), remoteAddressHash: "h1");

        var keys = ChatRateLimitKeyResolver.ResolveMessageKeys(context, new AIChatRateLimitingOptions());

        Assert.Equal(["user:u1", "ip-hash:h1"], keys);
    }

    [Fact]
    public void ResolveMessageKeys_Anonymous_ReturnsVisitorIpSessionAndConnectionKeys()
    {
        var context = CreateContext(
            visitorId: "v1",
            remoteAddressHash: "h1",
            sessionId: "s1",
            connectionId: "c1");

        var keys = ChatRateLimitKeyResolver.ResolveMessageKeys(context, new AIChatRateLimitingOptions());

        Assert.Equal(["visitor:v1", "ip-hash:h1", "session:s1", "conn:c1"], keys);
    }

    [Fact]
    public void ResolveMessageKeys_AnonymousWithNoIdentifyingData_ReturnsUnknown()
    {
        var keys = ChatRateLimitKeyResolver.ResolveMessageKeys(CreateContext(), new AIChatRateLimitingOptions());

        Assert.Equal(["unknown"], keys);
    }

    // ---- ResolveAuthenticatedUserKey ----

    [Fact]
    public void ResolveAuthenticatedUserKey_PrefersNameIdentifier()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "id-1"),
            new Claim(ClaimTypes.Name, "name-1"),
        ], "Test");
        var context = CreateContext(user: new ClaimsPrincipal(identity));

        Assert.Equal("user:id-1", ChatRateLimitKeyResolver.ResolveAuthenticatedUserKey(context));
    }

    [Fact]
    public void ResolveAuthenticatedUserKey_FallsBackToName()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "name-1")], "Test");
        var context = CreateContext(user: new ClaimsPrincipal(identity));

        Assert.Equal("user:name-1", ChatRateLimitKeyResolver.ResolveAuthenticatedUserKey(context));
    }

    [Fact]
    public void ResolveAuthenticatedUserKey_ReturnsNull_WhenNoIdentifier()
    {
        Assert.Null(ChatRateLimitKeyResolver.ResolveAuthenticatedUserKey(CreateContext()));
    }

    // ---- ResolveContextKeys ----

    [Fact]
    public void ResolveContextKeys_NeverIncludesUserKey()
    {
        var context = CreateContext(user: UserWithNameIdentifier("u1"), remoteAddressHash: "h1");

        var keys = ChatRateLimitKeyResolver.ResolveContextKeys(
            context,
            ChatRateLimitPartition.AuthenticatedUser | ChatRateLimitPartition.NetworkAddress);

        Assert.Equal(["ip-hash:h1"], keys);
    }

    [Fact]
    public void ResolveContextKeys_RespectsPartitionFlags()
    {
        var context = CreateContext(visitorId: "v1", remoteAddressHash: "h1", sessionId: "s1");

        var keys = ChatRateLimitKeyResolver.ResolveContextKeys(
            context,
            ChatRateLimitPartition.Visitor | ChatRateLimitPartition.Session);

        // Only the requested partitions are included; the IP hash is excluded.
        Assert.Equal(["visitor:v1", "session:s1"], keys);
    }

    // ---- ResolveAnonymousSessionStartKeys ----

    [Fact]
    public void ResolveAnonymousSessionStartKeys_UsesVisitorAndNetworkAddressByDefault()
    {
        var context = CreateContext(visitorId: "v1", remoteAddressHash: "h1", sessionId: "s1", connectionId: "c1");

        var keys = ChatRateLimitKeyResolver.ResolveAnonymousSessionStartKeys(context, new AIChatRateLimitingOptions());

        // Session-start defaults to Visitor + NetworkAddress only.
        Assert.Equal(["visitor:v1", "ip-hash:h1"], keys);
    }

    // ---- ResolveNetworkAddressKey (visitor identity) ----

    [Fact]
    public void ResolveNetworkAddressKey_PrefersHashOverPlainAddress()
    {
        var key = ChatRateLimitKeyResolver.ResolveNetworkAddressKey(new AIVisitorIdentity
        {
            RemoteAddressHash = "h1",
            RemoteAddress = "203.0.113.5",
        });

        Assert.Equal("ip-hash:h1", key);
    }

    [Fact]
    public void ResolveNetworkAddressKey_FallsBackToPlainAddress()
    {
        var key = ChatRateLimitKeyResolver.ResolveNetworkAddressKey(new AIVisitorIdentity
        {
            RemoteAddress = "203.0.113.5",
        });

        Assert.Equal("ip:203.0.113.5", key);
    }

    [Fact]
    public void ResolveNetworkAddressKey_ReturnsNull_WhenNoAddress()
    {
        Assert.Null(ChatRateLimitKeyResolver.ResolveNetworkAddressKey(new AIVisitorIdentity()));
    }

    // The plain-text visitor mode stores the full address and computes no hash, so the limiter would key on the full
    // address and a client rotating inside its /64 would get a fresh allowance for every address.
    [Theory]
    [InlineData("2001:db8:1:2::1")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd")]
    [InlineData("2001:0db8:0001:0002:ffff:ffff:ffff:ffff")]
    public void ResolveNetworkAddressKey_KeysAPlainIPv6AddressOnItsSlash64(string remoteAddress)
    {
        var key = ChatRateLimitKeyResolver.ResolveNetworkAddressKey(new AIVisitorIdentity { RemoteAddress = remoteAddress });

        Assert.Equal("ip:2001:db8:1:2::/64", key);
    }

    [Fact]
    public void ResolveNetworkAddressKey_KeysAnotherSlash64Apart()
    {
        var one = ChatRateLimitKeyResolver.ResolveNetworkAddressKey(new AIVisitorIdentity { RemoteAddress = "2001:db8:1:2::1" });
        var other = ChatRateLimitKeyResolver.ResolveNetworkAddressKey(new AIVisitorIdentity { RemoteAddress = "2001:db8:1:3::1" });

        Assert.NotEqual(one, other);
    }

    [Theory]
    [InlineData("::ffff:203.0.113.5", "ip:203.0.113.5")]
    [InlineData("203.0.113.5", "ip:203.0.113.5")]
    [InlineData(" 203.0.113.5 ", "ip:203.0.113.5")]
    [InlineData("unknown", "ip:unknown")]
    [InlineData("CfDJ8-encrypted-value", "ip:CfDJ8-encrypted-value")]
    public void ResolveNetworkAddressKey_KeepsAnythingThatIsNotAnIPv6AddressAsItIs(string remoteAddress, string expected)
    {
        Assert.Equal(expected, ChatRateLimitKeyResolver.ResolveNetworkAddressKey(new AIVisitorIdentity { RemoteAddress = remoteAddress }));
    }

    [Fact]
    public void ResolveMessageKeys_ForAPlainIPv6Address_KeysTheSlash64()
    {
        var one = ChatRateLimitKeyResolver.ResolveMessageKeys(CreateContext(remoteAddress: "2001:db8:1:2::1"), new AIChatRateLimitingOptions());
        var other = ChatRateLimitKeyResolver.ResolveMessageKeys(CreateContext(remoteAddress: "2001:db8:1:2:aaaa:bbbb:cccc:dddd"), new AIChatRateLimitingOptions());

        Assert.Contains("ip:2001:db8:1:2::/64", one);
        Assert.Equal(one, other);
    }

    private static ClaimsPrincipal UserWithNameIdentifier(string userId)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"));

    private static PromptSecurityContext CreateContext(
        ClaimsPrincipal user = null,
        string visitorId = null,
        string remoteAddressHash = null,
        string sessionId = null,
        string connectionId = null,
        string remoteAddress = null)
    {
        return new PromptSecurityContext
        {
            User = user,
            VisitorId = visitorId,
            RemoteAddressHash = remoteAddressHash,
            RemoteAddress = remoteAddress,
            SessionId = sessionId,
            ConnectionId = connectionId,
        };
    }
}
