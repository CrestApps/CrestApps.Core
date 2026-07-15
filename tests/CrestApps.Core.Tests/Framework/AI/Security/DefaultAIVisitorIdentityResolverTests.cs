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
