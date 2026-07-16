namespace CrestApps.Core.AI.Security;

/// <summary>
/// Per-profile anti-spam throttling overrides for an AI Profile.
/// Stored on <see cref="Models.AIProfile.Settings"/> using <c>profile.WithSettings(new PromptSecurityProfileSettings { ... })</c>.
/// When present, these values override the site-level throttling defaults defined on
/// <see cref="PromptSecurityOptions"/> for the specific profile, letting each use case raise or lower
/// its limits. High-level input and output security guards (injection detection, output filtering,
/// security preamble, input delimiters, blocking threshold, and maximum prompt length) are intentionally
/// not part of this model; those remain global concerns configured through <see cref="PromptSecurityOptions"/>.
/// </summary>
public sealed class PromptSecurityProfileSettings
{
    /// <summary>
    /// Gets or sets the maximum number of messages allowed within the rate limit window for this profile.
    /// When <see langword="null"/>, the site-level default is used.
    /// Set to <c>0</c> to explicitly disable message rate limiting for this profile.
    /// </summary>
    public int? MaxMessagesPerWindow { get; set; }

    /// <summary>
    /// Gets or sets the rate limit window duration for this profile.
    /// When <see langword="null"/>, the site-level default is used.
    /// </summary>
    public TimeSpan? RateLimitWindow { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of anonymous chat sessions that can be started
    /// within the anonymous session rate-limit window for this profile.
    /// When <see langword="null"/>, the site-level default is used.
    /// Set to <c>0</c> to explicitly disable anonymous session-start throttling for this profile.
    /// </summary>
    public int? MaxAnonymousSessionsPerWindow { get; set; }

    /// <summary>
    /// Gets or sets the anonymous session-start rate-limit window duration for this profile.
    /// When <see langword="null"/>, the site-level default is used.
    /// </summary>
    public TimeSpan? AnonymousSessionRateLimitWindow { get; set; }
}
