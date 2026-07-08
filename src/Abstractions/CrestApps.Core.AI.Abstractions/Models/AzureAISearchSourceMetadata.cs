namespace CrestApps.Core.AI.Models;

/// <summary>
/// Stores explicit Azure AI Search source connection settings for an AI data source.
/// </summary>
public sealed class AzureAISearchSourceMetadata
{
    /// <summary>
    /// The default Azure AI Search authentication type value.
    /// </summary>
    public const string DefaultAuthenticationType = "Default";

    /// <summary>
    /// The API key Azure AI Search authentication type value.
    /// </summary>
    public const string ApiKeyAuthenticationType = "ApiKey";

    /// <summary>
    /// The managed identity Azure AI Search authentication type value.
    /// </summary>
    public const string ManagedIdentityAuthenticationType = "ManagedIdentity";

    /// <summary>
    /// Gets or sets the Azure AI Search endpoint.
    /// </summary>
    public string Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the authentication type.
    /// </summary>
    public string AuthenticationType { get; set; }

    /// <summary>
    /// Gets or sets the remote Azure AI Search index name.
    /// </summary>
    public string IndexName { get; set; }

    /// <summary>
    /// Gets or sets the optional managed identity client identifier.
    /// </summary>
    public string IdentityClientId { get; set; }

    /// <summary>
    /// Gets or sets the protected admin or query API key.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets the normalized authentication type.
    /// </summary>
    public string GetAuthenticationType()
    {
        if (string.IsNullOrWhiteSpace(AuthenticationType))
        {
            return string.IsNullOrWhiteSpace(ApiKey)
                ? DefaultAuthenticationType
                : ApiKeyAuthenticationType;
        }

        var authenticationType = AuthenticationType.Trim();

        if (string.Equals(authenticationType, ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            return ApiKeyAuthenticationType;
        }

        if (string.Equals(authenticationType, ManagedIdentityAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            return ManagedIdentityAuthenticationType;
        }

        return DefaultAuthenticationType;
    }
}
