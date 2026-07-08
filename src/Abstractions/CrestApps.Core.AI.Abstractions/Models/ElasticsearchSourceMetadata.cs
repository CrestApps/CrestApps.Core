namespace CrestApps.Core.AI.Models;

/// <summary>
/// Stores explicit Elasticsearch source connection settings for an AI data source.
/// </summary>
public sealed class ElasticsearchSourceMetadata
{
    /// <summary>
    /// The anonymous Elasticsearch authentication type value.
    /// </summary>
    public const string NoneAuthenticationType = "None";

    /// <summary>
    /// The basic Elasticsearch authentication type value.
    /// </summary>
    public const string BasicAuthenticationType = "Basic";

    /// <summary>
    /// Gets or sets the Elasticsearch endpoint URL.
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// Gets or sets the authentication type.
    /// </summary>
    public string AuthenticationType { get; set; }

    /// <summary>
    /// Gets or sets the remote Elasticsearch index name.
    /// </summary>
    public string IndexName { get; set; }

    /// <summary>
    /// Gets or sets the optional username for basic authentication.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    /// Gets or sets the optional protected password for basic authentication.
    /// </summary>
    public string Password { get; set; }

    /// <summary>
    /// Gets or sets the optional TLS certificate fingerprint.
    /// </summary>
    public string CertificateFingerprint { get; set; }

    /// <summary>
    /// Gets the normalized authentication type.
    /// </summary>
    public string GetAuthenticationType()
    {
        if (string.IsNullOrWhiteSpace(AuthenticationType))
        {
            return string.IsNullOrWhiteSpace(Username) && string.IsNullOrWhiteSpace(Password)
                ? NoneAuthenticationType
                : BasicAuthenticationType;
        }

        return string.Equals(AuthenticationType.Trim(), BasicAuthenticationType, StringComparison.OrdinalIgnoreCase)
            ? BasicAuthenticationType
            : NoneAuthenticationType;
    }
}
