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
