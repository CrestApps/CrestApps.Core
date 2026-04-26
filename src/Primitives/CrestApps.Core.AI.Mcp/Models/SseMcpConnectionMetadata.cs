using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Mcp.Models;

/// <summary>
/// Represents the sse MCP Connection Metadata.
/// </summary>
public sealed class SseMcpConnectionMetadata : IConnectionAuthMetadata
{
    /// <summary>
    /// Gets or sets the endpoint.
    /// </summary>
    public Uri Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the authentication Type.
    /// </summary>
    public ClientAuthenticationType AuthenticationType { get; set; }

    // API Key authentication.

    /// <summary>
    /// Gets or sets the api Key Header Name.
    /// </summary>
    public string ApiKeyHeaderName { get; set; }

    /// <summary>
    /// Gets or sets the api Key Prefix.
    /// </summary>
    public string ApiKeyPrefix { get; set; }

    /// <summary>
    /// Gets or sets the api Key.
    /// </summary>
    public string ApiKey { get; set; }

    // Basic authentication.

    /// <summary>
    /// Gets or sets the basic Username.
    /// </summary>
    public string BasicUsername { get; set; }

    /// <summary>
    /// Gets or sets the basic Password.
    /// </summary>
    public string BasicPassword { get; set; }

    // OAuth 2.0 Client Credentials.

    /// <summary>
    /// Gets or sets the O Auth 2 Token Endpoint.
    /// </summary>
    public string OAuth2TokenEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the O Auth 2 Client ID.
    /// </summary>
    public string OAuth2ClientId { get; set; }

    /// <summary>
    /// Gets or sets the O Auth 2 Client Secret.
    /// </summary>
    public string OAuth2ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the O Auth 2 Scopes.
    /// </summary>
    public string OAuth2Scopes { get; set; }

    // OAuth 2.0 Private Key JWT.

    /// <summary>
    /// Gets or sets the O Auth 2 Private Key.
    /// </summary>
    public string OAuth2PrivateKey { get; set; }

    /// <summary>
    /// Gets or sets the O Auth 2 Key ID.
    /// </summary>
    public string OAuth2KeyId { get; set; }

    // OAuth 2.0 Mutual TLS (mTLS).

    /// <summary>
    /// Gets or sets the O Auth 2 Client Certificate.
    /// </summary>
    public string OAuth2ClientCertificate { get; set; }

    /// <summary>
    /// Gets or sets the O Auth 2 Client Certificate Password.
    /// </summary>
    public string OAuth2ClientCertificatePassword { get; set; }

    // Custom headers (advanced / legacy).

    /// <summary>
    /// Gets or sets the additional Headers.
    /// </summary>
    public Dictionary<string, string> AdditionalHeaders { get; set; }

}
