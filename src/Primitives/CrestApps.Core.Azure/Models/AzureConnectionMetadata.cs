namespace CrestApps.Core.Azure.Models;

/// <summary>
/// Represents the azure Connection Metadata.
/// </summary>
public class AzureConnectionMetadata
{
    /// <summary>
    /// Gets or sets the endpoint.
    /// </summary>
    public Uri Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the authentication Type.
    /// </summary>
    public AzureAuthenticationType AuthenticationType { get; set; }

    /// <summary>
    /// Gets or sets the api Key.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the I Dentity ID.
    /// </summary>
    public string IdentityId { get; set; }
}
