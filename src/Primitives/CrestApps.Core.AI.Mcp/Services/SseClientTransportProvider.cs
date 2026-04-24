using System.Text;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace CrestApps.Core.AI.Mcp.Services;

public sealed class SseClientTransportProvider : IMcpClientTransportProvider
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IOAuth2TokenService _oauth2TokenService;
    private readonly ILogger _logger;

    public SseClientTransportProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOAuth2TokenService oauth2TokenService,
        ILogger<SseClientTransportProvider> logger)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _oauth2TokenService = oauth2TokenService;
        _logger = logger;
    }

    public bool CanHandle(McpConnection connection)
    {
        return connection.Source == McpConstants.TransportTypes.Sse;
    }

    public async Task<IClientTransport> GetAsync(McpConnection connection)
    {
        if (!connection.TryGet<SseMcpConnectionMetadata>(out var metadata))
        {
            return null;
        }

        var headers = await BuildHeadersAsync(metadata);

        return new HttpClientTransport(new HttpClientTransportOptions { Endpoint = metadata.Endpoint, AdditionalHeaders = headers, });
    }

    private async Task<Dictionary<string, string>> BuildHeadersAsync(SseMcpConnectionMetadata metadata)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var protector = _dataProtectionProvider.CreateProtector(McpConstants.DataProtectionPurpose);
        switch (metadata.AuthenticationType)
        {
            case McpClientAuthenticationType.ApiKey:
                if (!string.IsNullOrEmpty(metadata.ApiKey))
                {
                    var apiKey = DataProtectionHelper.Unprotect(protector, metadata.ApiKey, _logger, "Failed to unprotect a credential value for MCP SSE connection.");
                    var headerName = string.IsNullOrWhiteSpace(metadata.ApiKeyHeaderName) ? "Authorization" : metadata.ApiKeyHeaderName;
                    headers[headerName] = !string.IsNullOrWhiteSpace(metadata.ApiKeyPrefix) ? $"{metadata.ApiKeyPrefix} {apiKey}" : apiKey;
                }

                break;
            case McpClientAuthenticationType.Basic:
                if (!string.IsNullOrEmpty(metadata.BasicUsername))
                {
                    var password = !string.IsNullOrEmpty(metadata.BasicPassword) ? DataProtectionHelper.Unprotect(protector, metadata.BasicPassword, _logger, "Failed to unprotect a credential value for MCP SSE connection.") : string.Empty;
                    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{metadata.BasicUsername}:{password}"));
                    headers["Authorization"] = $"Basic {credentials}";
                }

                break;
            case McpClientAuthenticationType.OAuth2ClientCredentials:
                if (!string.IsNullOrEmpty(metadata.OAuth2TokenEndpoint) && !string.IsNullOrEmpty(metadata.OAuth2ClientId) && !string.IsNullOrEmpty(metadata.OAuth2ClientSecret))
                {
                    var clientSecret = DataProtectionHelper.Unprotect(protector, metadata.OAuth2ClientSecret, _logger, "Failed to unprotect a credential value for MCP SSE connection.");
                    var token = await _oauth2TokenService.AcquireTokenAsync(metadata.OAuth2TokenEndpoint, metadata.OAuth2ClientId, clientSecret, metadata.OAuth2Scopes);
                    headers["Authorization"] = $"Bearer {token}";
                }

                break;
            case McpClientAuthenticationType.OAuth2PrivateKeyJwt:
                if (!string.IsNullOrEmpty(metadata.OAuth2TokenEndpoint) && !string.IsNullOrEmpty(metadata.OAuth2ClientId) && !string.IsNullOrEmpty(metadata.OAuth2PrivateKey))
                {
                    var privateKey = DataProtectionHelper.Unprotect(protector, metadata.OAuth2PrivateKey, _logger, "Failed to unprotect a credential value for MCP SSE connection.");
                    var token = await _oauth2TokenService.AcquireTokenWithPrivateKeyJwtAsync(metadata.OAuth2TokenEndpoint, metadata.OAuth2ClientId, privateKey, metadata.OAuth2KeyId, metadata.OAuth2Scopes);
                    headers["Authorization"] = $"Bearer {token}";
                }

                break;
            case McpClientAuthenticationType.OAuth2Mtls:
                if (!string.IsNullOrEmpty(metadata.OAuth2TokenEndpoint) && !string.IsNullOrEmpty(metadata.OAuth2ClientId) && !string.IsNullOrEmpty(metadata.OAuth2ClientCertificate))
                {
                    var certificateBytes = Convert.FromBase64String(DataProtectionHelper.Unprotect(protector, metadata.OAuth2ClientCertificate, _logger, "Failed to unprotect a credential value for MCP SSE connection."));
                    var certificatePassword = !string.IsNullOrEmpty(metadata.OAuth2ClientCertificatePassword) ? DataProtectionHelper.Unprotect(protector, metadata.OAuth2ClientCertificatePassword, _logger, "Failed to unprotect a credential value for MCP SSE connection.") : null;
                    var token = await _oauth2TokenService.AcquireTokenWithMtlsAsync(metadata.OAuth2TokenEndpoint, metadata.OAuth2ClientId, certificateBytes, certificatePassword, metadata.OAuth2Scopes);
                    headers["Authorization"] = $"Bearer {token}";
                }

                break;
            case McpClientAuthenticationType.CustomHeaders:
                if (metadata.AdditionalHeaders is not null)
                {
                    foreach (var header in metadata.AdditionalHeaders)
                    {
                        headers[header.Key] = header.Value;
                    }
                }

                break;
        }

        return headers;
    }
}
