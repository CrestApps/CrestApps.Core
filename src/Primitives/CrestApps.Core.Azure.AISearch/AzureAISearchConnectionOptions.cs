namespace CrestApps.Core.Azure.AISearch;

/// <summary>
/// Options for configuring an Azure AI Search connection.
/// Bind from configuration (e.g. "CrestApps:AzureAISearch").
/// </summary>
public sealed class AzureAISearchConnectionOptions
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
    /// The Azure AI Search service endpoint (e.g. "https://my-search.search.windows.net").
    /// </summary>
    public string Endpoint { get; set; }

    /// <summary>
    /// The admin API key used for authentication.
    /// When empty, <c>DefaultAzureCredential</c> is used instead unless <see cref="AuthenticationType"/>
    /// explicitly requires API key authentication.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Optional prefix applied to MVC-managed remote index names.
    /// </summary>
    public string IndexPrefix { get; set; }

    /// <summary>
    /// Optional authentication mode.
    /// Supported values are <c>Default</c>, <c>ApiKey</c>, and <c>ManagedIdentity</c>.
    /// </summary>
    public string AuthenticationType { get; set; }

    /// <summary>
    /// Optional managed identity client ID used when <c>DefaultAzureCredential</c> authenticates with Azure.
    /// </summary>
    public string IdentityClientId { get; set; }

    /// <summary>
    /// Backward-compatible alias for <see cref="IndexPrefix"/>.
    /// </summary>
    public string IndexesPrefix { get; set; }

    /// <summary>
    /// Gets the configured index prefix, including backward-compatible aliases.
    /// </summary>
    public string GetResolvedIndexPrefix()
    {
        if (!string.IsNullOrWhiteSpace(IndexPrefix))
        {
            return IndexPrefix;
        }

        return IndexesPrefix;
    }

    /// <summary>
    /// Gets a value indicating whether API key authentication was selected explicitly.
    /// </summary>
    public bool UsesApiKeyAuthentication()
    {
        return string.Equals(GetAuthenticationType(), ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the configured authentication type.
    /// </summary>
    public string GetAuthenticationType()
    {
        if (string.IsNullOrWhiteSpace(AuthenticationType))
        {
            return DefaultAuthenticationType;
        }

        var normalizedAuthenticationType = AuthenticationType.Trim();

        if (string.Equals(normalizedAuthenticationType, ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            return ApiKeyAuthenticationType;
        }

        if (string.Equals(normalizedAuthenticationType, ManagedIdentityAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            return ManagedIdentityAuthenticationType;
        }

        return DefaultAuthenticationType;
    }
}
