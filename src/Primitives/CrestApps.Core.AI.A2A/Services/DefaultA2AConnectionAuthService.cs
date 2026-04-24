using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.A2A.Models;
using CrestApps.Core.AI.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.A2A.Services;

internal sealed class DefaultA2AConnectionAuthService : IA2AConnectionAuthService
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IOAuth2TokenService _oauth2TokenService;
    private readonly ILogger _logger;

    public DefaultA2AConnectionAuthService(IDataProtectionProvider dataProtectionProvider, IOAuth2TokenService oauth2TokenService, ILogger<DefaultA2AConnectionAuthService> logger)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _oauth2TokenService = oauth2TokenService;
        _logger = logger;
    }

    public async Task<Dictionary<string, string>> BuildHeadersAsync(A2AConnectionMetadata metadata, CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (metadata is null)
        {
            return headers;
        }

        var protector = _dataProtectionProvider.CreateProtector(A2AConstants.DataProtectionPurpose);
        switch (metadata.AuthenticationType)
        {
            case A2AClientAuthenticationType.ApiKey:
                BuildApiKeyHeaders(metadata, protector, headers);
                break;
            case A2AClientAuthenticationType.Basic:
                BuildBasicHeaders(metadata, protector, headers);
                break;
            case A2AClientAuthenticationType.OAuth2ClientCredentials:
                await BuildOAuth2ClientCredentialsHeadersAsync(metadata, protector, headers, cancellationToken);
                break;
            case A2AClientAuthenticationType.OAuth2PrivateKeyJwt:
                await BuildOAuth2PrivateKeyJwtHeadersAsync(metadata, protector, headers, cancellationToken);
                break;
            case A2AClientAuthenticationType.OAuth2Mtls:
                await BuildOAuth2MtlsHeadersAsync(metadata, protector, headers, cancellationToken);
                break;
            case A2AClientAuthenticationType.CustomHeaders:
                BuildCustomHeaders(metadata, headers);
                break;
        }

        return headers;
    }

    public async Task ConfigureHttpClientAsync(HttpClient httpClient, A2AConnectionMetadata metadata, CancellationToken cancellationToken = default)
    {
        var headers = await BuildHeadersAsync(metadata, cancellationToken);
        foreach (var header in headers)
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private void BuildApiKeyHeaders(A2AConnectionMetadata metadata, IDataProtector protector, Dictionary<string, string> headers)
    {
        if (string.IsNullOrEmpty(metadata.ApiKey))
        {
            return;
        }

        var apiKey = DataProtectionHelper.Unprotect(protector, metadata.ApiKey, _logger, "Failed to unprotect a credential value for A2A connection.");
        var headerName = string.IsNullOrWhiteSpace(metadata.ApiKeyHeaderName) ? "Authorization" : metadata.ApiKeyHeaderName;
        var value = !string.IsNullOrWhiteSpace(metadata.ApiKeyPrefix) ? $"{metadata.ApiKeyPrefix} {apiKey}" : apiKey;
        headers[headerName] = value;
    }

    private void BuildBasicHeaders(A2AConnectionMetadata metadata, IDataProtector protector, Dictionary<string, string> headers)
    {
        if (string.IsNullOrEmpty(metadata.BasicUsername))
        {
            return;
        }

        var password = !string.IsNullOrEmpty(metadata.BasicPassword) ? DataProtectionHelper.Unprotect(protector, metadata.BasicPassword, _logger, "Failed to unprotect a credential value for A2A connection.") : string.Empty;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{metadata.BasicUsername}:{password}"));
        headers["Authorization"] = $"Basic {credentials}";
    }

    private async Task BuildOAuth2ClientCredentialsHeadersAsync(A2AConnectionMetadata metadata, IDataProtector protector, Dictionary<string, string> headers, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(metadata.OAuth2TokenEndpoint) || string.IsNullOrEmpty(metadata.OAuth2ClientId) || string.IsNullOrEmpty(metadata.OAuth2ClientSecret))
        {
            return;
        }

        var clientSecret = DataProtectionHelper.Unprotect(protector, metadata.OAuth2ClientSecret, _logger, "Failed to unprotect a credential value for A2A connection.");
        try
        {
            var token = await _oauth2TokenService.AcquireTokenAsync(metadata.OAuth2TokenEndpoint, metadata.OAuth2ClientId, clientSecret, metadata.OAuth2Scopes, cancellationToken);
            headers["Authorization"] = $"Bearer {token}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire OAuth2 token from '{TokenEndpoint}'.", metadata.OAuth2TokenEndpoint);
            throw;
        }
    }

    private async Task BuildOAuth2PrivateKeyJwtHeadersAsync(A2AConnectionMetadata metadata, IDataProtector protector, Dictionary<string, string> headers, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(metadata.OAuth2TokenEndpoint) || string.IsNullOrEmpty(metadata.OAuth2ClientId) || string.IsNullOrEmpty(metadata.OAuth2PrivateKey))
        {
            return;
        }

        var privateKey = DataProtectionHelper.Unprotect(protector, metadata.OAuth2PrivateKey, _logger, "Failed to unprotect a credential value for A2A connection.");
        try
        {
            var token = await _oauth2TokenService.AcquireTokenWithPrivateKeyJwtAsync(metadata.OAuth2TokenEndpoint, metadata.OAuth2ClientId, privateKey, metadata.OAuth2KeyId, metadata.OAuth2Scopes, cancellationToken);
            headers["Authorization"] = $"Bearer {token}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire OAuth2 token via Private Key JWT from '{TokenEndpoint}'.", metadata.OAuth2TokenEndpoint);
            throw;
        }
    }

    private async Task BuildOAuth2MtlsHeadersAsync(A2AConnectionMetadata metadata, IDataProtector protector, Dictionary<string, string> headers, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(metadata.OAuth2TokenEndpoint) || string.IsNullOrEmpty(metadata.OAuth2ClientId) || string.IsNullOrEmpty(metadata.OAuth2ClientCertificate))
        {
            return;
        }

        var certBase64 = DataProtectionHelper.Unprotect(protector, metadata.OAuth2ClientCertificate, _logger, "Failed to unprotect a credential value for A2A connection.");
        var certBytes = Convert.FromBase64String(certBase64);
        var certPassword = !string.IsNullOrEmpty(metadata.OAuth2ClientCertificatePassword) ? DataProtectionHelper.Unprotect(protector, metadata.OAuth2ClientCertificatePassword, _logger, "Failed to unprotect a credential value for A2A connection.") : null;
        try
        {
            var token = await _oauth2TokenService.AcquireTokenWithMtlsAsync(metadata.OAuth2TokenEndpoint, metadata.OAuth2ClientId, certBytes, certPassword, metadata.OAuth2Scopes, cancellationToken);
            headers["Authorization"] = $"Bearer {token}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire OAuth2 token via mTLS from '{TokenEndpoint}'.", metadata.OAuth2TokenEndpoint);
            throw;
        }
    }

    private static void BuildCustomHeaders(A2AConnectionMetadata metadata, Dictionary<string, string> headers)
    {
        if (metadata.AdditionalHeaders is null)
        {
            return;
        }

        foreach (var header in metadata.AdditionalHeaders)
        {
            headers[header.Key] = header.Value;
        }
    }
}

