namespace CrestApps.Core.AI.Security;

/// <summary>
/// Represents the resolved visitor identity for the current AI chat request.
/// </summary>
public sealed class AIVisitorIdentity
{
    /// <summary>
    /// Gets or sets the stable visitor identifier.
    /// For authenticated users this is the user identifier; for anonymous users this is a long-lived visitor token.
    /// </summary>
    public string VisitorId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the visitor is authenticated.
    /// </summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>
    /// Gets or sets the hashed remote-address signal used only for abuse controls.
    /// </summary>
    public string RemoteAddressHash { get; set; }

    /// <summary>
    /// Gets or sets the captured remote-address value when plain-text or encrypted storage is enabled.
    /// </summary>
    public string RemoteAddress { get; set; }
}
