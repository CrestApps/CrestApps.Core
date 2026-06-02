using System.Diagnostics;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Security;

/// <summary>
/// Detects prompt injection patterns in user input using normalized regex-based heuristics and weighted scoring.
/// </summary>
public sealed class PromptInjectionPatternDetector
{
    private readonly ILogger<PromptInjectionPatternDetector> _logger;
    private readonly PromptSecurityRiskScoringEngine _riskScoringEngine;
    private readonly IEnumerable<IPromptSecurityRule> _rules;

    /// <summary>
    /// Initializes a new instance of the <see cref="PromptInjectionPatternDetector"/> class.
    /// </summary>
    /// <param name="inputNormalizer">The input normalizer.</param>
    /// <param name="rules">The registered prompt security rules.</param>
    /// <param name="riskScoringEngine">The risk scoring engine.</param>
    /// <param name="logger">The logger.</param>
    public PromptInjectionPatternDetector(
        IEnumerable<IPromptSecurityRule> rules,
        PromptSecurityRiskScoringEngine riskScoringEngine,
        ILogger<PromptInjectionPatternDetector> logger)
    {
        _rules = rules;
        _riskScoringEngine = riskScoringEngine;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates the supplied prompt for known prompt injection indicators.
    /// </summary>
    /// <param name="context">The evaluation context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<PromptSecurityResult> EvaluateAsync(
        PromptSecurityEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(context.OriginalInput))
        {
            return PromptSecurityResult.Safe;
        }

        if (context.OriginalInput.Length > context.MaxPromptLength)
        {
            return PromptSecurityResult.Evaluated(
                PromptSecurityDisposition.Blocked,
                PromptRiskLevel.Medium,
                context.MaxPromptLength,
                "Prompt exceeds the maximum allowed length.",
                "max-length",
                [
                    new PromptSecurityRuleResult
                    {
                        RuleId = "max-length",
                        Categories = [PromptSecurityRuleCategories.InstructionOverride],
                        Severity = PromptRiskLevel.Medium,
                        Score = context.MaxPromptLength,
                        Reason = "Prompt exceeded the configured maximum length.",
                        Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["ruleType"] = "preflight",
                        },
                    },
                ],
                [PromptSecurityRuleCategories.InstructionOverride],
                new PromptSecurityDetectionTelemetry
                {
                    OriginalLength = context.OriginalInput.Length,
                });
        }

        var stopwatch = Stopwatch.StartNew();
        var normalizedContext = PromptSecurityInputNormalizer.Normalize(
            context.OriginalInput,
            context.MaxPromptLength,
            context.BlockingThreshold);
        var matchedRules = new List<PromptSecurityRuleResult>();

        foreach (var rule in _rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await rule.EvaluateAsync(normalizedContext, cancellationToken);

            if (result == null)
            {
                continue;
            }

            matchedRules.Add(result);
        }

        stopwatch.Stop();
        var evaluationResult = _riskScoringEngine.BuildResult(
            normalizedContext,
            matchedRules,
            stopwatch.Elapsed.TotalMilliseconds);

        if (evaluationResult.IsFlagged && _logger.IsEnabled(LogLevel.Debug))
        {
            var matchedRuleList = string.Join(", ", evaluationResult.MatchedRuleIds);

            _logger.LogDebug(
                "Prompt security detection matched {RuleCount} rule(s) with score {Score}. PrimaryRule={RuleId}. Rules={Rules}",
                evaluationResult.MatchedRuleIds.Count,
                evaluationResult.Score,
                evaluationResult.DetectionRule,
                matchedRuleList);
        }

        return evaluationResult;
    }
}
