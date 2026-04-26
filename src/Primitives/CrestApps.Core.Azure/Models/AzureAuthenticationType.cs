namespace CrestApps.Core.Azure.Models;

/// <summary>
/// Specifies the azure Authentication Type.
/// </summary>
public enum AzureAuthenticationType
{
    /// <summary>
    /// The default value.
    /// </summary>
    Default,

    /// <summary>
    /// The api Key value.
    /// </summary>
    ApiKey,

    /// <summary>
    /// The managed I Dentity value.
    /// </summary>
    ManagedIdentity,
}
