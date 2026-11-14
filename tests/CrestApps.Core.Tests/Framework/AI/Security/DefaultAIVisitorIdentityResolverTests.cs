using CrestApps.Core.AI.Chat.Security;
using CrestApps.Core.AI.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Framework.AI.Security;

public sealed class DefaultAIVisitorIdentityResolverTests
{
    [Fact]
    public void Resolve_WhenHashedModeEnabled_ReturnsHashOnly()
    {
        var resolver = CreateResolver(AIVisitorRemoteAddressMode.Hashed, "203.0.113.5");

        var identity = resolver.Resolve();

        Assert.NotNull(identity.VisitorId);
        Assert.Null(identity.RemoteAddress);
        Assert.False(string.IsNullOrWhiteSpace(identity.RemoteAddressHash));
    }

    [Fact]
    public void Resolve_WhenPlainTextModeEnabled_ReturnsPlainTextAddressOnly()
    {
        var resolver = CreateResolver(AIVisitorRemoteAddressMode.PlainText, "198.51.100.7");

        var identity = resolver.Resolve();

        Assert.Equal("198.51.100.7", identity.RemoteAddress);
        Assert.Null(identity.RemoteAddressHash);
    }

    [Fact]
    public void Resolve_WhenRemoteAddressCaptureDisabled_ReturnsNoAddressData()
    {
        var resolver = CreateResolver(AIVisitorRemoteAddressMode.Disabled, "192.0.2.20");

        var identity = resolver.Resolve();

        Assert.Null(identity.RemoteAddress);
        Assert.Null(identity.RemoteAddressHash);
    }

    [Fact]
    public void Resolve_WhenEncryptedModeEnabled_ReturnsEncryptedAddressAndHash()
    {
        var resolver = CreateResolver(AIVisitorRemoteAddressMode.Encrypted, "192.0.2.21");

        var identity = resolver.Resolve();

        Assert.NotNull(identity.RemoteAddress);
        Assert.NotEqual("192.0.2.21", identity.RemoteAddress);
        Assert.False(string.IsNullOrWhiteSpace(identity.RemoteAddressHash));
    }

    [Fact]
    public void Resolve_IgnoresAnXForwardedForHeaderTheCallerSent()
    {
        // A header any client can send must never pick the address the rate limiters key on. The forwarded
        // headers middleware replaces the connection's address when the host trusts its proxy.
        var httpContext = CreateHttpContext(isHttps: true);
        httpContext.Request.Headers["X-Forwarded-For"] = "198.51.100.99, 10.0.0.1";
        var options = new AIVisitorIdentityOptions
        {
            RemoteAddressMode = AIVisitorRemoteAddressMode.PlainText,
        };

        var identity = CreateResolver(options, httpContext).Resolve();

        Assert.Equal("203.0.113.5", identity.RemoteAddress);
    }

    [Fact]
    public void Resolve_KeysTheAddressHashOnTheConnectionAddressNotTheHeader()
    {
        var spoofed = CreateHttpContext(isHttps: true);
        spoofed.Request.Headers["X-Forwarded-For"] = "198.51.100.99";
        var plain = CreateHttpContext(isHttps: true);

        var spoofedHash = CreateResolver(new AIVisitorIdentityOptions(), spoofed).Resolve().RemoteAddressHash;
        var plainHash = CreateResolver(new AIVisitorIdentityOptions(), plain).Resolve().RemoteAddressHash;

        Assert.Equal(plainHash, spoofedHash);
    }

    [Fact]
    public void Resolve_ByDefault_WritesTheVisitorCookieAsSameSiteLax()
    {
        // SameSite=Lax is the right default for a chat served from its own site, and turning the new
        // option on must be the only thing that changes it.
        var setCookie = ResolveAndReadSetCookieHeader(new AIVisitorIdentityOptions());

        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("samesite=none", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("partitioned", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_WhenCrossSiteEmbeddingEnabled_WritesACookieAFrameCanKeep()
    {
        var options = new AIVisitorIdentityOptions
        {
            AllowCrossSiteEmbedding = true,
        };

        var setCookie = ResolveAndReadSetCookieHeader(options);

        Assert.Contains("samesite=none", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("partitioned", setCookie, StringComparison.OrdinalIgnoreCase);

        // The cookie identifies a visitor, it does not authenticate one, and it stays out of reach of
        // script either way.
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_WhenPartitioningTurnedOff_LeavesTheAttributeOut()
    {
        var options = new AIVisitorIdentityOptions
        {
            AllowCrossSiteEmbedding = true,
            UsePartitionedCookie = false,
        };

        var setCookie = ResolveAndReadSetCookieHeader(options);

        Assert.Contains("samesite=none", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("partitioned", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_WhenCrossSiteEmbeddingEnabledOverPlainHttp_KeepsTheLaxCookie()
    {
        // SameSite=None is only legal together with Secure, and a Secure cookie never reaches a plain
        // HTTP page. Emitting it here would drop the cookie altogether instead of saving it.
        var options = new AIVisitorIdentityOptions
        {
            AllowCrossSiteEmbedding = true,
        };

        var setCookie = ResolveAndReadSetCookieHeader(options, isHttps: false);

        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("samesite=none", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("partitioned", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_WhenTheVisitorCookieAlreadyExists_ReusesItAndWritesNothing(bool allowCrossSiteEmbedding)
    {
        var options = new AIVisitorIdentityOptions
        {
            AllowCrossSiteEmbedding = allowCrossSiteEmbedding,
        };

        var httpContext = CreateHttpContext(isHttps: true);
        httpContext.Request.Headers.Cookie = $"{options.CookieName}=existing-visitor";

        var identity = CreateResolver(options, httpContext).Resolve();

        Assert.Equal("existing-visitor", identity.VisitorId);
        Assert.False(identity.IsAuthenticated);
        Assert.Equal(0, httpContext.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public void Resolve_WithNoConnectionAddress_UsesOneSharedBucketInsteadOfNoKey()
    {
        // A null address (a Unix socket, a named pipe) would leave only keys the client can rotate, so the limiters would
        // fail open. Every such request lands in one bucket instead.
        var first = CreateResolverFor(null).Resolve();
        var second = CreateResolverFor(null).Resolve();

        Assert.False(string.IsNullOrWhiteSpace(first.RemoteAddressHash));
        Assert.Equal(first.RemoteAddressHash, second.RemoteAddressHash);
    }

    [Fact]
    public void Resolve_TreatsAnIPv4MappedAddressAsTheIPv4Address()
    {
        var mapped = CreateResolverFor(System.Net.IPAddress.Parse("::ffff:203.0.113.5"), AIVisitorRemoteAddressMode.PlainText).Resolve();
        var plain = CreateResolverFor(System.Net.IPAddress.Parse("203.0.113.5"), AIVisitorRemoteAddressMode.PlainText).Resolve();

        Assert.Equal("203.0.113.5", mapped.RemoteAddress);
        Assert.Equal(plain.RemoteAddress, mapped.RemoteAddress);
        Assert.Equal(
            CreateResolverFor(System.Net.IPAddress.Parse("::ffff:203.0.113.5")).Resolve().RemoteAddressHash,
            CreateResolverFor(System.Net.IPAddress.Parse("203.0.113.5")).Resolve().RemoteAddressHash);
    }

    [Fact]
    public void Resolve_KeysAnIPv6AddressOnItsSlash64SoRotatingWithinItDoesNotBuyANewAllowance()
    {
        var one = CreateResolverFor(System.Net.IPAddress.Parse("2001:db8:1:2::1")).Resolve();
        var other = CreateResolverFor(System.Net.IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:dddd")).Resolve();
        var elsewhere = CreateResolverFor(System.Net.IPAddress.Parse("2001:db8:1:3::1")).Resolve();

        Assert.Equal(one.RemoteAddressHash, other.RemoteAddressHash);
        Assert.NotEqual(one.RemoteAddressHash, elsewhere.RemoteAddressHash);
    }

    [Fact]
    public void Resolve_KeepsTheFullIPv6AddressWhereItIsStoredInPlainText()
    {
        var identity = CreateResolverFor(System.Net.IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:dddd"), AIVisitorRemoteAddressMode.PlainText).Resolve();

        Assert.Equal("2001:db8:1:2:aaaa:bbbb:cccc:dddd", identity.RemoteAddress);
    }

    [Fact]
    public void Resolve_CalledTwiceInOneRequest_GivesTheSameNewVisitorAndWritesOneCookie()
    {
        // Two services resolving the visitor in one request, as the session manager and an endpoint do, must agree: the
        // session is stamped with the first id and the browser keeps the cookie of the second.
        var httpContext = CreateHttpContext(isHttps: true);
        var resolver = CreateResolver(new AIVisitorIdentityOptions(), httpContext);

        var first = resolver.Resolve();
        var second = resolver.Resolve();

        Assert.Equal(first.VisitorId, second.VisitorId);
        Assert.Single(httpContext.Response.Headers.SetCookie);
    }

    [Theory]
    [InlineData("0123456789ABCDEFGHJKMNPQRS", true)]
    [InlineData("existing-visitor_1", true)]
    [InlineData("has space", false)]
    [InlineData("<script>", false)]
    [InlineData("quote'quote", false)]
    public void Resolve_OnlyReusesAVisitorCookieThatLooksLikeAnId(string cookieValue, bool reused)
    {
        var httpContext = CreateHttpContext(isHttps: true);
        httpContext.Request.Headers.Cookie = $"crestapps-ai-visitor={cookieValue}";

        var identity = CreateResolver(new AIVisitorIdentityOptions(), httpContext).Resolve();

        Assert.Equal(reused, identity.VisitorId == cookieValue);
        Assert.Equal(reused ? 0 : 1, httpContext.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public void Resolve_ReplacesAVisitorCookieTooLongForTheIndexedColumn()
    {
        // The value goes into a 255-character indexed column on the session, and the client chooses it.
        var httpContext = CreateHttpContext(isHttps: true);
        httpContext.Request.Headers.Cookie = $"crestapps-ai-visitor={new string('a', 300)}";

        var identity = CreateResolver(new AIVisitorIdentityOptions(), httpContext).Resolve();

        Assert.True(identity.VisitorId.Length <= 64);
        Assert.Single(httpContext.Response.Headers.SetCookie);
    }

    private static DefaultAIVisitorIdentityResolver CreateResolverFor(System.Net.IPAddress address, AIVisitorRemoteAddressMode mode = AIVisitorRemoteAddressMode.Hashed)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = address;

        return CreateResolver(new AIVisitorIdentityOptions { RemoteAddressMode = mode, RemoteAddressHashSalt = "test-salt" }, httpContext);
    }

    private static string ResolveAndReadSetCookieHeader(AIVisitorIdentityOptions options, bool isHttps = true)
    {
        var httpContext = CreateHttpContext(isHttps);

        CreateResolver(options, httpContext).Resolve();

        return Assert.Single(httpContext.Response.Headers.SetCookie);
    }

    private static DefaultHttpContext CreateHttpContext(bool isHttps)
    {
        var httpContext = new DefaultHttpContext();

        httpContext.Request.IsHttps = isHttps;
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.5");

        return httpContext;
    }

    private static DefaultAIVisitorIdentityResolver CreateResolver(AIVisitorIdentityOptions options, HttpContext httpContext)
        => new(
            new HttpContextAccessor
            {
                HttpContext = httpContext,
            },
            Options.Create(options),
            new TestHostEnvironment(),
            new EphemeralDataProtectionProvider());

    private static DefaultAIVisitorIdentityResolver CreateResolver(AIVisitorRemoteAddressMode mode, string remoteAddress)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteAddress);

        return new DefaultAIVisitorIdentityResolver(
            new HttpContextAccessor
            {
                HttpContext = httpContext,
            },
            Options.Create(new AIVisitorIdentityOptions
            {
                RemoteAddressMode = mode,
                RemoteAddressHashSalt = "test-salt",
            }),
            new TestHostEnvironment(),
            new EphemeralDataProtectionProvider());
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "CrestApps.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
    }
}
