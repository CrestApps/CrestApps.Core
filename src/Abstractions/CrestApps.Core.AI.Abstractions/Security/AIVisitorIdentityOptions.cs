namespace CrestApps.Core.AI.Security;

/// <summary>
/// Configures anonymous visitor tracking for AI chat widgets and sessions.
/// </summary>
public sealed class AIVisitorIdentityOptions
{
    /// <summary>
    /// Gets or sets the cookie name used to persist the anonymous visitor identifier.
    /// </summary>
    public string CookieName { get; set; } = "crestapps-ai-visitor";

    /// <summary>
    /// Gets or sets how long the anonymous visitor cookie remains valid.
    /// </summary>
    public TimeSpan CookieLifetime { get; set; } = TimeSpan.FromDays(180);

    /// <summary>
    /// Gets or sets how the remote address should be captured for abuse controls and optional auditing.
    /// </summary>
    public AIVisitorRemoteAddressMode RemoteAddressMode { get; set; } = AIVisitorRemoteAddressMode.Hashed;

    /// <summary>
    /// Gets or sets an application-specific salt used when hashing remote addresses.
    /// This value is used when <see cref="RemoteAddressMode"/> is set to <see cref="AIVisitorRemoteAddressMode.Hashed"/>
    /// or <see cref="AIVisitorRemoteAddressMode.Encrypted"/>.
    /// </summary>
    public string RemoteAddressHashSalt { get; set; } = "CrestApps.Core.AI.VisitorIdentity";
}
