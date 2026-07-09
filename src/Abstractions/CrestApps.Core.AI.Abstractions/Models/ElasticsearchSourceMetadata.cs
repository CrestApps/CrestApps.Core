namespace CrestApps.Core.AI.Models;

/// <summary>
/// Stores explicit Elasticsearch source connection settings for an AI data source.
/// </summary>
public sealed class ElasticsearchSourceMetadata
{
    /// <summary>
    /// The self-managed Elasticsearch environment type value.
    /// </summary>
    public const string SelfManagedEnvironmentType = "SelfManaged";

    /// <summary>
    /// The Elastic Cloud hosted environment type value.
    /// </summary>
    public const string CloudHostedEnvironmentType = "CloudHosted";

    /// <summary>
    /// The anonymous Elasticsearch authentication type value.
    /// </summary>
    public const string NoneAuthenticationType = "None";

    /// <summary>
    /// The basic Elasticsearch authentication type value.
    /// </summary>
    public const string BasicAuthenticationType = "Basic";

    /// <summary>
    /// The Elasticsearch API key authentication type value.
    /// </summary>
    public const string ApiKeyAuthenticationType = "ApiKey";

    /// <summary>
    /// The Elasticsearch base64-encoded API key authentication type value.
    /// </summary>
    public const string Base64ApiKeyAuthenticationType = "Base64ApiKey";

    /// <summary>
    /// The Elasticsearch key identifier plus API key authentication type value.
    /// </summary>
    public const string KeyIdAndKeyAuthenticationType = "KeyIdAndKey";

    /// <summary>
    /// Gets or sets the Elasticsearch environment type.
    /// </summary>
    public string EnvironmentType { get; set; }

    /// <summary>
    /// Gets or sets the Elasticsearch endpoint URL.
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// Gets or sets the Elastic Cloud deployment identifier.
    /// </summary>
    public string CloudId { get; set; }

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
    /// Gets or sets the optional protected Elasticsearch API key value.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the optional protected base64-encoded Elasticsearch API key value.
    /// </summary>
    public string Base64ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the optional Elasticsearch API key identifier.
    /// </summary>
    public string ApiKeyId { get; set; }

    /// <summary>
    /// Gets or sets the optional TLS certificate fingerprint.
    /// </summary>
    public string CertificateFingerprint { get; set; }

    /// <summary>
    /// Gets the normalized environment type.
    /// </summary>
    public string GetEnvironmentType()
    {
        if (string.IsNullOrWhiteSpace(EnvironmentType))
        {
            return string.IsNullOrWhiteSpace(CloudId)
                ? SelfManagedEnvironmentType
                : CloudHostedEnvironmentType;
        }

        var environmentType = EnvironmentType.Trim();

        if (string.Equals(environmentType, CloudHostedEnvironmentType, StringComparison.OrdinalIgnoreCase))
        {
            return CloudHostedEnvironmentType;
        }

        return SelfManagedEnvironmentType;
    }

    /// <summary>
    /// Gets the normalized authentication type.
    /// </summary>
    public string GetAuthenticationType()
    {
        if (string.IsNullOrWhiteSpace(AuthenticationType))
        {
            if (!string.IsNullOrWhiteSpace(Username) || !string.IsNullOrWhiteSpace(Password))
            {
                return BasicAuthenticationType;
            }

            if (!string.IsNullOrWhiteSpace(ApiKeyId) || !string.IsNullOrWhiteSpace(ApiKey))
            {
                return string.IsNullOrWhiteSpace(ApiKeyId)
                    ? ApiKeyAuthenticationType
                    : KeyIdAndKeyAuthenticationType;
            }

            if (!string.IsNullOrWhiteSpace(Base64ApiKey))
            {
                return Base64ApiKeyAuthenticationType;
            }

            return NoneAuthenticationType;
        }

        var authenticationType = AuthenticationType.Trim();

        if (string.Equals(authenticationType, BasicAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            return BasicAuthenticationType;
        }

        if (string.Equals(authenticationType, ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            return ApiKeyAuthenticationType;
        }

        if (string.Equals(authenticationType, Base64ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            return Base64ApiKeyAuthenticationType;
        }

        if (string.Equals(authenticationType, KeyIdAndKeyAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            return KeyIdAndKeyAuthenticationType;
        }

        return NoneAuthenticationType;
    }
}
