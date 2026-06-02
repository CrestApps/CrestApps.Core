namespace CrestApps.Core.AI.Security;

/// <summary>
/// Represents the result of a prompt security evaluation.
/// </summary>
public sealed class PromptSecurityResult
{
    /// <summary>
    /// A shared instance representing a safe result with no detected risk.
    /// </summary>
    public static readonly PromptSecurityResult Safe = new();

    /// <summary>
    /// Gets a value indicating whether suspicious indicators were detected.
    /// </summary>
    public bool IsFlagged => Disposition != PromptSecurityDisposition.Safe;

    /// <summary>
    /// Gets or sets a value indicating whether the prompt was blocked.
    /// </summary>
    public bool IsBlocked { get; init; }

    /// <summary>
    /// Gets or sets the final disposition of the evaluation.
    /// </summary>
    public PromptSecurityDisposition Disposition { get; init; }

    /// <summary>
    /// Gets or sets the risk level assessed for the prompt.
    /// </summary>
    public PromptRiskLevel RiskLevel { get; init; }

    /// <summary>
    /// Gets or sets the aggregate weighted score assigned to the prompt.
    /// </summary>
    public int Score { get; init; }

    /// <summary>
    /// Gets or sets the reason the prompt was blocked or flagged.
    /// </summary>
    public string Reason { get; init; }

    /// <summary>
    /// Gets or sets the primary detection rule associated with the result.
    /// When multiple rules match, this is the highest-scoring rule identifier.
    /// </summary>
    public string DetectionRule { get; init; }

    /// <summary>
    /// Gets or sets the matched rule identifiers associated with the evaluation.
    /// </summary>
    public IReadOnlyList<string> MatchedRuleIds { get; init; } = [];

    /// <summary>
    /// Gets or sets the matched rule details associated with the evaluation.
    /// </summary>
    public IReadOnlyList<PromptSecurityRuleResult> MatchedRules { get; init; } = [];

    /// <summary>
    /// Gets or sets the distinct matched categories associated with the evaluation.
    /// </summary>
    public IReadOnlyList<string> MatchedCategories { get; init; } = [];

    /// <summary>
    /// Gets or sets the normalization and evaluation telemetry associated with the evaluation.
    /// </summary>
    public PromptSecurityDetectionTelemetry Telemetry { get; init; } = PromptSecurityDetectionTelemetry.Empty;

    /// <summary>
    /// Creates a blocked result with the specified parameters.
    /// </summary>
    /// <param name="reason">The reason for blocking.</param>
    /// <param name="riskLevel">The risk level.</param>
    /// <param name="detectionRule">The rule that triggered the block.</param>
    public static PromptSecurityResult Blocked(string reason, PromptRiskLevel riskLevel = PromptRiskLevel.High, string detectionRule = null)
    {
        return new PromptSecurityResult
        {
            IsBlocked = true,
            Disposition = PromptSecurityDisposition.Blocked,
            Reason = reason,
            RiskLevel = riskLevel,
            DetectionRule = detectionRule,
            MatchedRuleIds = string.IsNullOrWhiteSpace(detectionRule) ? [] : [detectionRule],
        };
    }

    /// <summary>
    /// Creates a flagged (not blocked but suspicious) result.
    /// </summary>
    /// <param name="reason">The reason for flagging.</param>
    /// <param name="riskLevel">The risk level.</param>
    /// <param name="detectionRule">The rule that triggered the flag.</param>
    public static PromptSecurityResult Flagged(string reason, PromptRiskLevel riskLevel = PromptRiskLevel.Medium, string detectionRule = null)
    {
        return new PromptSecurityResult
        {
            IsBlocked = false,
            Disposition = PromptSecurityDisposition.Flagged,
            Reason = reason,
            RiskLevel = riskLevel,
            DetectionRule = detectionRule,
            MatchedRuleIds = string.IsNullOrWhiteSpace(detectionRule) ? [] : [detectionRule],
        };
    }

    /// <summary>
    /// Creates an evaluation result from weighted rule matches and computed telemetry.
    /// </summary>
    /// <param name="disposition">The final evaluation disposition.</param>
    /// <param name="riskLevel">The aggregate risk level.</param>
    /// <param name="score">The aggregate weighted score.</param>
    /// <param name="reason">The summarized evaluation reason.</param>
    /// <param name="detectionRule">The primary matched rule identifier.</param>
    /// <param name="matchedRules">The matched rules.</param>
    /// <param name="matchedCategories">The matched categories.</param>
    /// <param name="telemetry">The evaluation telemetry.</param>
    public static PromptSecurityResult Evaluated(
        PromptSecurityDisposition disposition,
        PromptRiskLevel riskLevel,
        int score,
        string reason,
        string detectionRule,
        IReadOnlyList<PromptSecurityRuleResult> matchedRules,
        IReadOnlyList<string> matchedCategories,
        PromptSecurityDetectionTelemetry telemetry)
    {
        return new PromptSecurityResult
        {
            IsBlocked = disposition == PromptSecurityDisposition.Blocked,
            Disposition = disposition,
            RiskLevel = riskLevel,
            Score = score,
            Reason = reason,
            DetectionRule = detectionRule,
            MatchedRules = matchedRules ?? [],
            MatchedRuleIds = matchedRules?.Select(static x => x.RuleId).ToArray() ?? [],
            MatchedCategories = matchedCategories ?? [],
            Telemetry = telemetry ?? PromptSecurityDetectionTelemetry.Empty,
        };
    }
}
