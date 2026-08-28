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
    /// Gets or sets a value indicating whether user messages in Chat Interactions are wrapped with
    /// boundary delimiters. Chat Interactions do not receive the security preamble or injection
    /// blocking (the operator controls the prompt, model, and tools), but clear input boundaries still
    /// help the model avoid confusing the user's message with system, tool, or agent content — which
    /// matters most when many agents and tools are involved.
    /// </summary>
    public bool EnableChatInteractionInputDelimiters { get; set; } = true;

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
    /// Gets or sets the maximum number of messages per window that an authenticated caller can send.
    /// Set to zero to disable message rate limiting for authenticated callers. Anonymous callers are
    /// governed by <see cref="AnonymousMessageRateLimitTiers"/> instead (falling back to this value
    /// and <see cref="RateLimitWindow"/> only when no tiers are configured).
    /// </summary>
    public int MaxMessagesPerWindow { get; set; } = 20;

    /// <summary>
    /// Gets or sets the rate limit window duration used with <see cref="MaxMessagesPerWindow"/>.
    /// </summary>
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the multi-tier sliding-window message limits applied to <em>anonymous</em>
    /// callers. A message is throttled when it would exceed any tier. When empty, anonymous callers
    /// fall back to the single <see cref="MaxMessagesPerWindow"/> / <see cref="RateLimitWindow"/>
    /// window. Authenticated callers are never governed by these tiers.
    /// </summary>
    public List<ChatRateLimitTier> AnonymousMessageRateLimitTiers { get; set; } =
    [
        new() { Limit = 5, Window = TimeSpan.FromSeconds(30) },
        new() { Limit = 30, Window = TimeSpan.FromMinutes(5) },
        new() { Limit = 150, Window = TimeSpan.FromHours(1) },
        new() { Limit = 500, Window = TimeSpan.FromDays(1) },
    ];

    /// <summary>
    /// Gets or sets the maximum number of anonymous chat sessions that can be started within
    /// <see cref="AnonymousSessionRateLimitWindow"/>. Used only as a fallback when
    /// <see cref="AnonymousSessionStartRateLimitTiers"/> is empty. Set to zero to disable the
    /// fallback limit.
    /// </summary>
    public int MaxAnonymousSessionsPerWindow { get; set; } = 20;

    /// <summary>
    /// Gets or sets the anonymous session-start rate-limit window duration used with
    /// <see cref="MaxAnonymousSessionsPerWindow"/>.
    /// </summary>
    public TimeSpan AnonymousSessionRateLimitWindow { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets or sets the multi-tier sliding-window limits applied to <em>anonymous</em> session
    /// starts. A session start is throttled when it would exceed any tier. When empty, anonymous
    /// session starts fall back to the single <see cref="MaxAnonymousSessionsPerWindow"/> /
    /// <see cref="AnonymousSessionRateLimitWindow"/> window. Authenticated callers never start
    /// rate-limited sessions.
    /// </summary>
    public List<ChatRateLimitTier> AnonymousSessionStartRateLimitTiers { get; set; } =
    [
        new() { Limit = 5, Window = TimeSpan.FromSeconds(30) },
        new() { Limit = 10, Window = TimeSpan.FromMinutes(5) },
        new() { Limit = 150, Window = TimeSpan.FromHours(1) },
        new() { Limit = 500, Window = TimeSpan.FromDays(1) },
    ];
}
