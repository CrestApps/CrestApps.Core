using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Security;

/// <summary>
/// Aggregates rule matches into a weighted prompt security result.
/// </summary>
public sealed class PromptSecurityRiskScoringEngine
{
    private readonly IOptions<PromptSecurityOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PromptSecurityRiskScoringEngine"/> class.
    /// </summary>
    /// <param name="options">The prompt security options.</param>
    public PromptSecurityRiskScoringEngine(IOptions<PromptSecurityOptions> options)
    {
        _options = options;
    }

    /// <summary>
    /// Builds the aggregate security result from the matched rules and evaluation telemetry.
    /// </summary>
    /// <param name="context">The evaluation context.</param>
    /// <param name="matchedRules">The matched rules.</param>
    /// <param name="evaluationDurationMilliseconds">The elapsed evaluation duration.</param>
    public PromptSecurityResult BuildResult(
        PromptSecurityEvaluationContext context,
        IReadOnlyList<PromptSecurityRuleResult> matchedRules,
        double evaluationDurationMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(matchedRules);

        var matchedCategories = matchedRules
            .SelectMany(static x => x.Categories)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var score = matchedRules.Sum(static x => x.Score);
        score += GetRuleCombinationBonus(matchedRules.Count);
        score += GetCategoryCombinationBonus(matchedCategories.Length);

        if (matchedRules.Any(static x => x.MatchedOnFoldedInput))
        {
            score += 6;
        }

        if ((context.Telemetry.RemovedZeroWidthCharacterCount > 0 || context.Telemetry.HomoglyphReplacementCount > 0) && matchedRules.Count > 0)
        {
            score += 4;
        }

        var riskLevel = DetermineRiskLevel(score);
        var disposition = DetermineDisposition(riskLevel, context.BlockingThreshold);
        var primaryRule = matchedRules
            .OrderByDescending(static x => x.Score)
            .ThenByDescending(static x => x.Severity)
            .Select(static x => x.RuleId)
            .FirstOrDefault();
        var reason = BuildReason(matchedRules, disposition, riskLevel);

        var telemetry = context.Telemetry ?? PromptSecurityDetectionTelemetry.Empty;
        telemetry.MatchedRuleCount = matchedRules.Count;
        telemetry.DistinctCategoryCount = matchedCategories.Length;
        telemetry.EvaluationDurationMilliseconds = evaluationDurationMilliseconds;

        return PromptSecurityResult.Evaluated(
            disposition,
            riskLevel,
            score,
            reason,
            primaryRule,
            matchedRules,
            matchedCategories,
            telemetry);
    }

    private PromptRiskLevel DetermineRiskLevel(int score)
    {
        var options = _options.Value;

        if (score >= options.CriticalRiskScoreThreshold)
        {
            return PromptRiskLevel.Critical;
        }

        if (score >= options.HighRiskScoreThreshold)
        {
            return PromptRiskLevel.High;
        }

        if (score >= options.MediumRiskScoreThreshold)
        {
            return PromptRiskLevel.Medium;
        }

        if (score >= options.LowRiskScoreThreshold)
        {
            return PromptRiskLevel.Low;
        }

        return PromptRiskLevel.None;
    }

    private static PromptSecurityDisposition DetermineDisposition(PromptRiskLevel riskLevel, PromptRiskLevel blockingThreshold)
    {
        if (riskLevel == PromptRiskLevel.None)
        {
            return PromptSecurityDisposition.Safe;
        }

        if (riskLevel >= blockingThreshold)
        {
            return PromptSecurityDisposition.Blocked;
        }

        return PromptSecurityDisposition.Flagged;
    }

    private static int GetRuleCombinationBonus(int ruleCount)
    {
        return ruleCount > 1
            ? Math.Min(12, (ruleCount - 1) * 3)
            : 0;
    }

    private static int GetCategoryCombinationBonus(int categoryCount)
    {
        return categoryCount > 1
            ? Math.Min(10, (categoryCount - 1) * 2)
            : 0;
    }

    private static string BuildReason(
        IReadOnlyList<PromptSecurityRuleResult> matchedRules,
        PromptSecurityDisposition disposition,
        PromptRiskLevel riskLevel)
    {
        if (matchedRules.Count == 0)
        {
            return disposition == PromptSecurityDisposition.Safe
                ? "No prompt injection indicators detected."
                : "Suspicious prompt indicators detected.";
        }

        var topReasons = matchedRules
            .OrderByDescending(static x => x.Score)
            .Select(static x => x.Reason)
            .Distinct(StringComparer.Ordinal)
            .Take(3);

        return $"{disposition} prompt at {riskLevel} risk. Indicators: {string.Join("; ", topReasons)}";
    }
}
