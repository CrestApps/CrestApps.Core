namespace CrestApps.Core.AI.Security;

/// <summary>
/// Configuration options for prompt security features.
/// </summary>
public sealed class PromptSecurityOptions
{
    /// <summary>
    /// Gets or sets the maximum allowed prompt length in characters.
    /// Prompts exceeding this length are automatically blocked.
    /// </summary>
    public int MaxPromptLength { get; set; } = 8000;

    /// <summary>
    /// Gets or sets a value indicating whether injection pattern detection is enabled.
    /// </summary>
    public bool EnableInjectionDetection { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether output security filtering is enabled.
    /// </summary>
    public bool EnableOutputFiltering { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a hardened security preamble is prepended
    /// to system prompts.
    /// </summary>
    public bool EnableSecurityPreamble { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether user messages are wrapped with boundary
    /// delimiters to help the model distinguish instructions from user content.
    /// </summary>
    public bool EnableInputDelimiters { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether security audit logging is enabled.
    /// </summary>
    public bool EnableAuditLogging { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum risk level at which prompts are blocked.
    /// Prompts at or above this level will be rejected after weighted scoring has been applied.
    /// </summary>
    public PromptRiskLevel BlockingThreshold { get; set; } = PromptRiskLevel.High;

    /// <summary>
    /// Gets or sets the minimum aggregate score required for a prompt to become suspicious.
    /// Scores below this threshold are treated as safe.
    /// </summary>
    public int LowRiskScoreThreshold { get; set; } = 10;

    /// <summary>
    /// Gets or sets the minimum aggregate score required for a prompt to be classified as medium risk.
    /// </summary>
    public int MediumRiskScoreThreshold { get; set; } = 20;

    /// <summary>
    /// Gets or sets the minimum aggregate score required for a prompt to be classified as high risk.
    /// </summary>
    public int HighRiskScoreThreshold { get; set; } = 35;

    /// <summary>
    /// Gets or sets the minimum aggregate score required for a prompt to be classified as critical risk.
    /// </summary>
    public int CriticalRiskScoreThreshold { get; set; } = 50;

    /// <summary>
    /// Gets or sets additional regex patterns to detect in user prompts.
    /// Matches against these patterns contribute a critical score and are blocked by default thresholds.
    /// </summary>
    public List<string> CustomBlockedPatterns { get; set; } = [];

    /// <summary>
    /// Gets or sets the maximum number of messages per session that can be sent
    /// within the rate limit window. Set to zero to disable rate limiting.
    /// </summary>
    public int MaxMessagesPerWindow { get; set; } = 20;

    /// <summary>
    /// Gets or sets the rate limit window duration.
    /// </summary>
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the maximum number of anonymous chat sessions that can be started
    /// within the anonymous session rate-limit window. Set to zero to disable this limit.
    /// </summary>
    public int MaxAnonymousSessionsPerWindow { get; set; } = 5;

    /// <summary>
    /// Gets or sets the anonymous session-start rate-limit window duration.
    /// </summary>
    public TimeSpan AnonymousSessionRateLimitWindow { get; set; } = TimeSpan.FromMinutes(10);
}
