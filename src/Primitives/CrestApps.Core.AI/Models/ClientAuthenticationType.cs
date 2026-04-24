namespace CrestApps.Core.AI.Models;

/// <summary>
/// Specifies the authentication mechanism used for protocol connections (MCP, A2A, etc.).
/// </summary>
public enum ClientAuthenticationType
{
    /// <summary>
    /// No authentication required.
    /// </summary>
    Anonymous,

    /// <summary>
    /// API key-based authentication.
    /// </summary>
    ApiKey,

    /// <summary>
    /// HTTP Basic authentication.
    /// </summary>
    Basic,

    /// <summary>
    /// OAuth 2.0 Client Credentials grant.
    /// </summary>
    OAuth2ClientCredentials,

    /// <summary>
    /// OAuth 2.0 Private Key JWT client assertion.
    /// </summary>
    OAuth2PrivateKeyJwt,

    /// <summary>
    /// OAuth 2.0 Mutual TLS (mTLS) client certificate authentication.
    /// </summary>
    OAuth2Mtls,

    /// <summary>
    /// Custom HTTP headers for advanced or legacy authentication.
    /// </summary>
    CustomHeaders,
}
