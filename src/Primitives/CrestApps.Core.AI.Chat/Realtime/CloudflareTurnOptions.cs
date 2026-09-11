namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Identifies the Cloudflare Realtime TURN key used to mint short-lived TURN credentials.
/// </summary>
/// <remarks>
/// Cloudflare does not expose a shared secret, so credentials cannot be signed locally the way coturn's
/// <c>use-auth-secret</c> allows. They are issued by an API call authenticated with the values below, which are
/// long-lived and belong in a secret store rather than in configuration files.
/// </remarks>
public sealed class CloudflareTurnOptions
{
    /// <summary>
    /// Gets or sets the TURN key identifier, shown as the TURN Token ID in the Cloudflare dashboard.
    /// </summary>
    public string KeyId { get; set; }

    /// <summary>
    /// Gets or sets the API token issued alongside the TURN key.
    /// </summary>
    public string ApiToken { get; set; }

    /// <summary>
    /// Gets or sets the lifetime, in seconds, requested for each set of credentials. Defaults to 24 hours.
    /// </summary>
    /// <remarks>
    /// Credentials are replaced once they are halfway through this lifetime, so the value only needs to
    /// comfortably exceed the longest call a caller might place. It is not a session limit — that is
    /// <see cref="Core.AI.Realtime.RealtimeTransportOptions.MaxSessionDurationSeconds"/>.
    /// </remarks>
    public int TtlSeconds { get; set; } = 86400;

    /// <summary>
    /// Gets or sets the base address of the Cloudflare Realtime API.
    /// </summary>
    /// <remarks>
    /// Overridable so a test can point at a stub, and so a future regional or proxied endpoint needs no code
    /// change. Defaults to the public endpoint.
    /// </remarks>
    public string ApiBaseAddress { get; set; } = "https://rtc.live.cloudflare.com";

    /// <summary>
    /// Gets a value indicating whether both credentials needed to call the Cloudflare API are present.
    /// </summary>
    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(ApiToken);
}
