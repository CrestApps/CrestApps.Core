namespace CrestApps.Core.Elasticsearch;

/// <summary>
/// Options for configuring an Elasticsearch connection.
/// Bind from configuration (e.g. "CrestApps:Elasticsearch").
/// </summary>
public sealed class ElasticsearchConnectionOptions
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
    /// The Elasticsearch server URL (e.g. "https://localhost:9200").
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// The optional Elastic Cloud deployment identifier.
    /// </summary>
    public string CloudId { get; set; }

    /// <summary>
    /// The authentication type.
    /// </summary>
    public string AuthenticationType { get; set; }

    /// <summary>
    /// Optional username for basic authentication.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    /// Optional password for basic authentication.
    /// </summary>
    public string Password { get; set; }

    /// <summary>
    /// Optional API key value for Elasticsearch API key authentication.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Optional base64-encoded API key value for Elasticsearch API key authentication.
    /// </summary>
    public string Base64ApiKey { get; set; }

    /// <summary>
    /// Optional API key identifier for Elasticsearch API key authentication.
    /// </summary>
    public string ApiKeyId { get; set; }

    /// <summary>
    /// Optional certificate fingerprint for TLS verification.
    /// </summary>
    public string CertificateFingerprint { get; set; }

    /// <summary>
    /// Optional prefix applied to MVC-managed remote index names.
    /// </summary>
    public string IndexPrefix { get; set; }

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
