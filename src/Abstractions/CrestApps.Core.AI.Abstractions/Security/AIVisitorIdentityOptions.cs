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
    /// Gets or sets a value indicating whether the visitor cookie is written so it survives inside a
    /// frame on another site. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The cookie is written <c>SameSite=Lax</c>, which a browser refuses in a third party context and
    /// reports as "Cookie ... has been rejected because it is in a cross-site context". A chat embedded
    /// in a frame on a customer's site therefore looks like a brand new visitor on every request: the
    /// conversation does not survive a page load, and the visitor rate-limit partition never
    /// accumulates, so it stops contributing to abuse control.
    /// <para>
    /// Turning this on writes the cookie <c>SameSite=None; Secure</c> instead, which is the only
    /// combination a browser keeps in a frame. Leave it off unless the chat really is embedded on other
    /// sites. The cookie stays <c>HttpOnly</c> either way, and it identifies a visitor rather than
    /// authenticating one, so it grants no privilege of its own.
    /// </para>
    /// <para>
    /// <c>SameSite=None</c> is only legal together with <c>Secure</c>, and a <c>Secure</c> cookie never
    /// reaches a plain HTTP page, so a request that did not arrive over HTTPS keeps the
    /// <c>SameSite=Lax</c> cookie rather than losing it altogether.
    /// </para>
    /// </remarks>
    public bool AllowCrossSiteEmbedding { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the visitor cookie carries the <c>Partitioned</c>
    /// attribute (CHIPS) when <see cref="AllowCrossSiteEmbedding"/> is on. Defaults to
    /// <see langword="true"/>, and there is rarely a reason to turn it off.
    /// </summary>
    /// <remarks>
    /// It does two things. Browsers that phase out unrestricted third party cookies still accept a
    /// partitioned one. More importantly, <c>SameSite</c> is not part of a cookie's identity key, so
    /// without partitioning the cookie written in a frame replaces the first party cookie of the same
    /// name and downgrades it to <c>SameSite=None</c> everywhere. The partition key <em>is</em> part of
    /// the key, so the two stay apart wherever the attribute is honoured.
    /// <para>
    /// A partitioned cookie gives a visitor a separate identifier per embedding site, which is what
    /// per-site abuse control wants. Turn it off only for a deployment that needs one identifier across
    /// sites and accepts that browsers are removing that ability.
    /// </para>
    /// </remarks>
    public bool UsePartitionedCookie { get; set; } = true;

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
