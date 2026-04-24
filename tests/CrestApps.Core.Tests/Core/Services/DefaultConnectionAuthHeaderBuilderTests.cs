using System.Text;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class DefaultConnectionAuthHeaderBuilderTests
{
    private const string TestPurpose = "TestPurpose";

    [Fact]
    public async Task BuildHeadersAsync_NullMetadata_ReturnsEmptyHeaders()
    {
        var builder = CreateBuilder();

        var headers = await builder.BuildHeadersAsync(null, TestPurpose, TestContext.Current.CancellationToken);

        Assert.Empty(headers);
    }

    [Fact]
    public async Task BuildHeadersAsync_Anonymous_ReturnsEmptyHeaders()
    {
        var builder = CreateBuilder();
        var metadata = CreateMetadata(ClientAuthenticationType.Anonymous);

        var headers = await builder.BuildHeadersAsync(metadata, TestPurpose, TestContext.Current.CancellationToken);

        Assert.Empty(headers);
    }

    [Fact]
    public async Task BuildHeadersAsync_ApiKey_DefaultHeader_SetsAuthorizationHeader()
    {
        var builder = CreateBuilder();
        var metadata = CreateMetadata(ClientAuthenticationType.ApiKey);
        metadata.ApiKey = "test-api-key";

        var headers = await builder.BuildHeadersAsync(metadata, TestPurpose, TestContext.Current.CancellationToken);

        Assert.Single(headers);
        Assert.Equal("test-api-key", headers["Authorization"]);
    }

    [Fact]
    public async Task BuildHeadersAsync_ApiKey_CustomHeader_SetsCustomHeader()
    {
        var builder = CreateBuilder();
        var metadata = CreateMetadata(ClientAuthenticationType.ApiKey);
        metadata.ApiKey = "my-key";
        metadata.ApiKeyHeaderName = "X-Api-Key";

        var headers = await builder.BuildHeadersAsync(metadata, TestPurpose, TestContext.Current.CancellationToken);

        Assert.Single(headers);
        Assert.Equal("my-key", headers["X-Api-Key"]);
    }

    [Fact]
    public async Task BuildHeadersAsync_ApiKey_WithPrefix_IncludesPrefix()
    {
        var builder = CreateBuilder();
        var metadata = CreateMetadata(ClientAuthenticationType.ApiKey);
        metadata.ApiKey = "my-key";
        metadata.ApiKeyPrefix = "Bearer";

        var headers = await builder.BuildHeadersAsync(metadata, TestPurpose, TestContext.Current.CancellationToken);

        Assert.Equal("Bearer my-key", headers["Authorization"]);
    }

    [Fact]
    public async Task BuildHeadersAsync_ApiKey_EmptyKey_ReturnsEmptyHeaders()
    {
        var builder = CreateBuilder();
        var metadata = CreateMetadata(ClientAuthenticationType.ApiKey);
        metadata.ApiKey = string.Empty;

        var headers = await builder.BuildHeadersAsync(metadata, TestPurpose, TestContext.Current.CancellationToken);

        Assert.Empty(headers);
    }

    [Fact]
    public async Task BuildHeadersAsync_Basic_SetsBasicHeader()
    {
        var builder = CreateBuilder();
        var metadata = CreateMetadata(ClientAuthenticationType.Basic);
        metadata.BasicUsername = "user";
        metadata.BasicPassword = "pass";

        var headers = await builder.BuildHeadersAsync(metadata, TestPurpose, TestContext.Current.CancellationToken);

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass"));
        Assert.Equal($"Basic {expected}", headers["Authorization"]);
    }

    [Fact]
    public async Task BuildHeadersAsync_Basic_EmptyPassword_UsesEmptyString()
    {
        var builder = CreateBuilder();
        var metadata = CreateMetadata(ClientAuthenticationType.Basic);
        metadata.BasicUsername = "user";
        metadata.BasicPassword = string.Empty;

        var headers = await builder.BuildHeadersAsync(metadata, TestPurpose, TestContext.Current.CancellationToken);

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:"));
        Assert.Equal($"Basic {expected}", headers["Authorization"]);
    }

    [Fact]
    public async Task BuildHeadersAsync_Basic_EmptyUsername_ReturnsEmptyHeaders()
    {
        var builder = CreateBuilder();
        var metadata = CreateMetadata(ClientAuthenticationType.Basic);
        metadata.BasicUsername = string.Empty;

        var headers = await builder.BuildHeadersAsync(metadata, TestPurpose, TestContext.Current.CancellationToken);

        Assert.Empty(headers);
    }

    [Fact]
    public async Task BuildHeadersAsync_OAuth2ClientCredentials_SetsBearerToken()
    {
        var tokenService = new Mock<IOAuth2TokenService>();
        tokenService
            .Setup(t => t.AcquireTokenAsync("https://auth.example.com/token", "client-id", "client-secret", "scope1 scope2", It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");

        var builder = CreateBuilder(tokenService.Object);
        var metadata = CreateMetadata(ClientAuthenticationType.OAuth2ClientCredentials);
        metadata.OAuth2TokenEndpoint = "https://auth.example.com/token";
        metadata.OAuth2ClientId = "client-id";
        metadata.OAuth2ClientSecret = "client-secret";
        metadata.OAuth2Scopes = "scope1 scope2";

        var headers = await builder.BuildHeadersAsync(metadata, TestPurpose, TestContext.Current.CancellationToken);

        Assert.Equal("Bearer test-token", headers["Authorization"]);
    }

    [Fact]
    public async Task BuildHeadersAsync_OAuth2ClientCredentials_MissingFields_ReturnsEmptyHeaders()
    {
        var builder = CreateBuilder();
        var metadata = CreateMetadata(ClientAuthenticationType.OAuth2ClientCredentials);
        metadata.OAuth2TokenEndpoint = "https://auth.example.com/token";
        metadata.OAuth2ClientId = "client-id";
        // Missing OAuth2ClientSecret

        var headers = await builder.BuildHeadersAsync(metadata, TestPurpose, TestContext.Current.CancellationToken);

        Assert.Empty(headers);
    }

    [Fact]
    public async Task BuildHeadersAsync_CustomHeaders_CopiesAllHeaders()
    {
        var builder = CreateBuilder();
        var metadata = CreateMetadata(ClientAuthenticationType.CustomHeaders);
        metadata.AdditionalHeaders = new Dictionary<string, string>
        {
            ["X-Custom-1"] = "value1",
            ["X-Custom-2"] = "value2",
        };

        var headers = await builder.BuildHeadersAsync(metadata, TestPurpose, TestContext.Current.CancellationToken);

        Assert.Equal(2, headers.Count);
        Assert.Equal("value1", headers["X-Custom-1"]);
        Assert.Equal("value2", headers["X-Custom-2"]);
    }

    [Fact]
    public async Task BuildHeadersAsync_CustomHeaders_NullDictionary_ReturnsEmptyHeaders()
    {
        var builder = CreateBuilder();
        var metadata = CreateMetadata(ClientAuthenticationType.CustomHeaders);
        metadata.AdditionalHeaders = null;

        var headers = await builder.BuildHeadersAsync(metadata, TestPurpose, TestContext.Current.CancellationToken);

        Assert.Empty(headers);
    }

    private static DefaultConnectionAuthHeaderBuilder CreateBuilder(IOAuth2TokenService tokenService = null)
    {
        var dataProtectionProvider = new PassthroughDataProtectionProvider();
        tokenService ??= Mock.Of<IOAuth2TokenService>();
        var logger = NullLogger<DefaultConnectionAuthHeaderBuilder>.Instance;

        return new DefaultConnectionAuthHeaderBuilder(dataProtectionProvider, tokenService, logger);
    }

    private static TestConnectionAuthMetadata CreateMetadata(ClientAuthenticationType authType)
    {
        return new TestConnectionAuthMetadata { AuthenticationType = authType };
    }

    private sealed class TestConnectionAuthMetadata : IConnectionAuthMetadata
    {
        public ClientAuthenticationType AuthenticationType { get; set; }
        public string ApiKeyHeaderName { get; set; }
        public string ApiKeyPrefix { get; set; }
        public string ApiKey { get; set; }
        public string BasicUsername { get; set; }
        public string BasicPassword { get; set; }
        public string OAuth2TokenEndpoint { get; set; }
        public string OAuth2ClientId { get; set; }
        public string OAuth2ClientSecret { get; set; }
        public string OAuth2Scopes { get; set; }
        public string OAuth2PrivateKey { get; set; }
        public string OAuth2KeyId { get; set; }
        public string OAuth2ClientCertificate { get; set; }
        public string OAuth2ClientCertificatePassword { get; set; }
        public Dictionary<string, string> AdditionalHeaders { get; set; }
    }

    private sealed class PassthroughDataProtectionProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) => new PassthroughDataProtector();
    }

    private sealed class PassthroughDataProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;
        public byte[] Protect(byte[] plaintext) => plaintext;
        public byte[] Unprotect(byte[] protectedData) => protectedData;
    }
}

