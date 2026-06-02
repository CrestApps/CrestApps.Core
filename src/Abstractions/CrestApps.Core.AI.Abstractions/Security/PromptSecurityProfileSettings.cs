namespace CrestApps.Core.AI.Security;

/// <summary>
/// Per-profile settings that control prompt security behavior.
/// Stored on <see cref="Models.AIProfile.Settings"/> using <c>profile.WithSettings(new PromptSecurityProfileSettings { ... })</c>.
/// When present, these settings override the site-level <see cref="PromptSecurityOptions"/> defaults
/// for the specific profile.
/// </summary>
public sealed class PromptSecurityProfileSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the prompt security layer is enabled for this profile.
    /// When <see langword="null"/>, the site-level default is used.
    /// Set to <see langword="false"/> to explicitly disable security for this profile.
    /// </summary>
    public bool? IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether injection pattern detection is enabled for this profile.
    /// When <see langword="null"/>, the site-level default is used.
    /// </summary>
    public bool? EnableInjectionDetection { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether output security filtering is enabled for this profile.
    /// When <see langword="null"/>, the site-level default is used.
    /// </summary>
    public bool? EnableOutputFiltering { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the hardened security preamble is prepended
    /// to the system prompt for this profile.
    /// When <see langword="null"/>, the site-level default is used.
    /// </summary>
    public bool? EnableSecurityPreamble { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether user messages are wrapped with boundary
    /// delimiters for this profile.
    /// When <see langword="null"/>, the site-level default is used.
    /// </summary>
    public bool? EnableInputDelimiters { get; set; }

    /// <summary>
    /// Gets or sets the maximum allowed prompt length for this profile.
    /// When <see langword="null"/>, the site-level default is used.
    /// </summary>
    public int? MaxPromptLength { get; set; }

    /// <summary>
    /// Gets or sets the minimum risk level at which prompts are blocked for this profile.
    /// When <see langword="null"/>, the site-level default is used.
    /// </summary>
    public PromptRiskLevel? BlockingThreshold { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of messages allowed within the rate limit window for this profile.
    /// When <see langword="null"/>, the site-level default is used.
    /// Set to <c>0</c> to explicitly disable rate limiting for this profile.
    /// </summary>
    public int? MaxMessagesPerWindow { get; set; }

    /// <summary>
    /// Gets or sets the rate limit window duration for this profile.
    /// When <see langword="null"/>, the site-level default is used.
    /// </summary>
    public TimeSpan? RateLimitWindow { get; set; }
}
