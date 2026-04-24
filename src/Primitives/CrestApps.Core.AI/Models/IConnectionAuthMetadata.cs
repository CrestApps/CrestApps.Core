namespace CrestApps.Core.AI.Models;

/// <summary>
/// Describes the authentication metadata for a protocol connection.
/// Implemented by protocol-specific metadata classes (MCP SSE, A2A, etc.)
/// to enable a shared authentication header builder.
/// </summary>
public interface IConnectionAuthMetadata
{
    /// <summary>
    /// Gets the authentication type for this connection.
    /// </summary>
    ClientAuthenticationType AuthenticationType { get; }

    /// <summary>
    /// Gets the custom header name for API key authentication.
    /// When <see langword="null"/> or empty, defaults to "Authorization".
    /// </summary>
    string ApiKeyHeaderName { get; }

    /// <summary>
    /// Gets the prefix for the API key value (e.g., "Bearer", "Api-Key").
    /// </summary>
    string ApiKeyPrefix { get; }

    /// <summary>
    /// Gets the protected API key value.
    /// </summary>
    string ApiKey { get; }

    /// <summary>
    /// Gets the username for HTTP Basic authentication.
    /// </summary>
    string BasicUsername { get; }

    /// <summary>
    /// Gets the protected password for HTTP Basic authentication.
    /// </summary>
    string BasicPassword { get; }

    /// <summary>
    /// Gets the OAuth 2.0 token endpoint URL.
    /// </summary>
    string OAuth2TokenEndpoint { get; }

    /// <summary>
    /// Gets the OAuth 2.0 client ID.
    /// </summary>
    string OAuth2ClientId { get; }

    /// <summary>
    /// Gets the protected OAuth 2.0 client secret.
    /// </summary>
    string OAuth2ClientSecret { get; }

    /// <summary>
    /// Gets the OAuth 2.0 scopes (space-separated).
    /// </summary>
    string OAuth2Scopes { get; }

    /// <summary>
    /// Gets the protected OAuth 2.0 private key for JWT client assertion.
    /// </summary>
    string OAuth2PrivateKey { get; }

    /// <summary>
    /// Gets the key ID for OAuth 2.0 Private Key JWT authentication.
    /// </summary>
    string OAuth2KeyId { get; }

    /// <summary>
    /// Gets the protected OAuth 2.0 client certificate (Base64-encoded).
    /// </summary>
    string OAuth2ClientCertificate { get; }

    /// <summary>
    /// Gets the protected password for the OAuth 2.0 client certificate.
    /// </summary>
    string OAuth2ClientCertificatePassword { get; }

    /// <summary>
    /// Gets additional custom HTTP headers.
    /// </summary>
    Dictionary<string, string> AdditionalHeaders { get; }
}
