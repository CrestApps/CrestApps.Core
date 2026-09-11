namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Identifies the Cloudflare Realtime TURN token used to mint short-lived TURN credentials.
/// </summary>
/// <remarks>
/// Cloudflare does not expose a shared secret, so credentials cannot be signed locally the way coturn's
/// <c>use-auth-secret</c> allows. They are issued by an API call authenticated with the values below, which are
/// long-lived and belong in a secret store rather than in configuration files.
/// </remarks>
public sealed class CloudflareTurnOptions
{
    private string _tokenId;
    private string _apiToken;

    /// <summary>
    /// Gets or sets the TURN Token ID, named as it appears in the Cloudflare dashboard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cloudflare's own API path spells the same value as a key -- <c>/v1/turn/keys/{id}</c> -- but the
    /// dashboard an operator copies it from calls it a Token ID, so that is the name used here.
    /// </para>
    /// <para>
    /// Trimmed on assignment. Whitespace is never part of either credential, and a value pasted out of a
    /// dashboard routinely carries some; trimming here rather than at the point of use means
    /// <see cref="IsConfigured"/> is the single answer to whether the value is usable.
    /// </para>
    /// </remarks>
    public string TokenId
    {
        get => _tokenId;
        set => _tokenId = value?.Trim();
    }

    /// <summary>
    /// Gets or sets the API token issued alongside the TURN Token ID.
    /// </summary>
    /// <remarks>
    /// Trimmed on assignment, as <see cref="TokenId"/> is. A trailing newline here is not cosmetic: it makes
    /// the <c>Authorization</c> header throw when the request is built.
    /// </remarks>
    public string ApiToken
    {
        get => _apiToken;
        set => _apiToken = value?.Trim();
    }

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
        => !string.IsNullOrWhiteSpace(TokenId) && !string.IsNullOrWhiteSpace(ApiToken);
}
