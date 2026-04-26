using System.Text;
using CrestApps.Core.AI.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

internal sealed class DefaultConnectionAuthHeaderBuilder : IConnectionAuthHeaderBuilder
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IOAuth2TokenService _oauth2TokenService;
    private readonly ILogger<DefaultConnectionAuthHeaderBuilder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultConnectionAuthHeaderBuilder"/> class.
    /// </summary>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    /// <param name="oauth2TokenService">The oauth 2 token service.</param>
    /// <param name="logger">The logger.</param>
    public DefaultConnectionAuthHeaderBuilder(
        IDataProtectionProvider dataProtectionProvider,
        IOAuth2TokenService oauth2TokenService,
        ILogger<DefaultConnectionAuthHeaderBuilder> logger)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _oauth2TokenService = oauth2TokenService;
        _logger = logger;
    }

    /// <summary>
    /// Builds headers.
    /// </summary>
    /// <param name="metadata">The metadata.</param>
    /// <param name="dataProtectionPurpose">The data protection purpose.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<Dictionary<string, string>> BuildHeadersAsync(
        IConnectionAuthMetadata metadata,
        string dataProtectionPurpose,
        CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (metadata is null)
        {
            return headers;
        }

        var protector = _dataProtectionProvider.CreateProtector(dataProtectionPurpose);

        switch (metadata.AuthenticationType)
        {
            case ClientAuthenticationType.ApiKey:
                BuildApiKeyHeaders(metadata, protector, headers);
                break;

            case ClientAuthenticationType.Basic:
                BuildBasicHeaders(metadata, protector, headers);
                break;

            case ClientAuthenticationType.OAuth2ClientCredentials:
                await BuildOAuth2ClientCredentialsHeadersAsync(metadata, protector, headers, cancellationToken);
                break;

            case ClientAuthenticationType.OAuth2PrivateKeyJwt:
                await BuildOAuth2PrivateKeyJwtHeadersAsync(metadata, protector, headers, cancellationToken);
                break;

            case ClientAuthenticationType.OAuth2Mtls:
                await BuildOAuth2MtlsHeadersAsync(metadata, protector, headers, cancellationToken);
                break;

            case ClientAuthenticationType.CustomHeaders:
                BuildCustomHeaders(metadata, headers);
                break;
        }

        return headers;
    }

    private void BuildApiKeyHeaders(IConnectionAuthMetadata metadata, IDataProtector protector, Dictionary<string, string> headers)
    {
        if (string.IsNullOrEmpty(metadata.ApiKey))
        {
            return;
        }

        var apiKey = DataProtectionHelper.Unprotect(protector, metadata.ApiKey, _logger, "Failed to unprotect API key credential.");
        var headerName = string.IsNullOrWhiteSpace(metadata.ApiKeyHeaderName) ? "Authorization" : metadata.ApiKeyHeaderName;
        headers[headerName] = !string.IsNullOrWhiteSpace(metadata.ApiKeyPrefix) ? $"{metadata.ApiKeyPrefix} {apiKey}" : apiKey;
    }

    private void BuildBasicHeaders(IConnectionAuthMetadata metadata, IDataProtector protector, Dictionary<string, string> headers)
    {
        if (string.IsNullOrEmpty(metadata.BasicUsername))
        {
            return;
        }

        var password = !string.IsNullOrEmpty(metadata.BasicPassword)
            ? DataProtectionHelper.Unprotect(protector, metadata.BasicPassword, _logger, "Failed to unprotect Basic password credential.")
            : string.Empty;

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{metadata.BasicUsername}:{password}"));
        headers["Authorization"] = $"Basic {credentials}";
    }

    private async Task BuildOAuth2ClientCredentialsHeadersAsync(
        IConnectionAuthMetadata metadata,
        IDataProtector protector,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(metadata.OAuth2TokenEndpoint) || string.IsNullOrEmpty(metadata.OAuth2ClientId) || string.IsNullOrEmpty(metadata.OAuth2ClientSecret))
        {
            return;
        }

        var clientSecret = DataProtectionHelper.Unprotect(protector, metadata.OAuth2ClientSecret, _logger, "Failed to unprotect OAuth2 client secret.");

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

    private async Task BuildOAuth2PrivateKeyJwtHeadersAsync(
        IConnectionAuthMetadata metadata,
        IDataProtector protector,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(metadata.OAuth2TokenEndpoint) || string.IsNullOrEmpty(metadata.OAuth2ClientId) || string.IsNullOrEmpty(metadata.OAuth2PrivateKey))
        {
            return;
        }

        var privateKey = DataProtectionHelper.Unprotect(protector, metadata.OAuth2PrivateKey, _logger, "Failed to unprotect OAuth2 private key.");

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

    private async Task BuildOAuth2MtlsHeadersAsync(
        IConnectionAuthMetadata metadata,
        IDataProtector protector,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(metadata.OAuth2TokenEndpoint) || string.IsNullOrEmpty(metadata.OAuth2ClientId) || string.IsNullOrEmpty(metadata.OAuth2ClientCertificate))
        {
            return;
        }

        var certBase64 = DataProtectionHelper.Unprotect(protector, metadata.OAuth2ClientCertificate, _logger, "Failed to unprotect OAuth2 client certificate.");
        var certBytes = Convert.FromBase64String(certBase64);
        var certPassword = !string.IsNullOrEmpty(metadata.OAuth2ClientCertificatePassword)
            ? DataProtectionHelper.Unprotect(protector, metadata.OAuth2ClientCertificatePassword, _logger, "Failed to unprotect OAuth2 certificate password.")
            : null;

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

    private static void BuildCustomHeaders(IConnectionAuthMetadata metadata, Dictionary<string, string> headers)
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
