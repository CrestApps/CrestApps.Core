using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Security;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares the captured LINQ-based prompt risk aggregation with production's single-pass
/// aggregation and bounded top-reason selection. This class must remain unsealed because
/// BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class PromptSecurityRiskScoringBenchmarks
{
    private PromptSecurityRiskScoringEngine _current;
    private LegacyPromptSecurityRiskScoringEngine _legacy;
    private PromptSecurityEvaluationContext _currentContext;
    private PromptSecurityEvaluationContext _legacyContext;
    private IReadOnlyList<PromptSecurityRuleResult> _rules;

    /// <summary>
    /// Gets or sets the number of matched rules to aggregate.
    /// </summary>
    [Params(0, 1, 4, 16, 32)]
    public int RuleCount { get; set; }

    /// <summary>
    /// Creates equivalent legacy and production inputs and verifies exact result semantics.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var options = Options.Create(new PromptSecurityOptions());
        _legacy = new LegacyPromptSecurityRiskScoringEngine(options);
        _current = new PromptSecurityRiskScoringEngine(options);
        _legacyContext = CreateContext();
        _currentContext = CreateContext();
        _rules = CreateRules(RuleCount);

        var legacyResult = _legacy.BuildResult(_legacyContext, _rules, 12.5);
        var currentResult = _current.BuildResult(_currentContext, _rules, 12.5);

        if (legacyResult.Disposition != currentResult.Disposition ||
            legacyResult.RiskLevel != currentResult.RiskLevel ||
            legacyResult.Score != currentResult.Score ||
            !string.Equals(legacyResult.Reason, currentResult.Reason, StringComparison.Ordinal) ||
            !string.Equals(legacyResult.DetectionRule, currentResult.DetectionRule, StringComparison.Ordinal) ||
            !legacyResult.MatchedRuleIds.SequenceEqual(currentResult.MatchedRuleIds) ||
            !legacyResult.MatchedCategories.SequenceEqual(currentResult.MatchedCategories) ||
            legacyResult.Telemetry.MatchedRuleCount != currentResult.Telemetry.MatchedRuleCount ||
            legacyResult.Telemetry.DistinctCategoryCount != currentResult.Telemetry.DistinctCategoryCount)
        {
            throw new InvalidOperationException("Prompt security risk scoring behavior differs.");
        }
    }

    /// <summary>
    /// Aggregates matched rules through the captured implementation.
    /// </summary>
    /// <returns>The aggregate prompt security result.</returns>
    [Benchmark(Baseline = true)]
    public PromptSecurityResult Legacy()
    {
        return _legacy.BuildResult(_legacyContext, _rules, 12.5);
    }

    /// <summary>
    /// Aggregates matched rules through production.
    /// </summary>
    /// <returns>The aggregate prompt security result.</returns>
    [Benchmark]
    public PromptSecurityResult Current()
    {
        return _current.BuildResult(_currentContext, _rules, 12.5);
    }

    private static PromptSecurityEvaluationContext CreateContext()
    {
        return new PromptSecurityEvaluationContext
        {
            BlockingThreshold = PromptRiskLevel.High,
            Telemetry = new PromptSecurityDetectionTelemetry
            {
                RemovedZeroWidthCharacterCount = 1,
                HomoglyphReplacementCount = 1,
            },
        };
    }

    private static PromptSecurityRuleResult[] CreateRules(int count)
    {
        var rules = new PromptSecurityRuleResult[count];

        for (var index = 0; index < count; index++)
        {
            rules[index] = new PromptSecurityRuleResult
            {
                RuleId = $"rule-{index}",
                Score = ((index % 5) + 1) * 7,
                Severity = (PromptRiskLevel)((index % 4) + 1),
                Reason = $"reason-{index % 6}",
                Categories = [$"category-{index % 8}", "shared"],
                MatchedOnFoldedInput = index % 7 == 0,
            };
        }

        return rules;
    }

    private sealed class LegacyPromptSecurityRiskScoringEngine(IOptions<PromptSecurityOptions> options)
    {
        public PromptSecurityResult BuildResult(
            PromptSecurityEvaluationContext context,
            IReadOnlyList<PromptSecurityRuleResult> matchedRules,
            double evaluationDurationMilliseconds)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(matchedRules);

            var matchedCategories = matchedRules
                .SelectMany(static rule => rule.Categories)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var score = matchedRules.Sum(static rule => rule.Score);
            score += GetRuleCombinationBonus(matchedRules.Count);
            score += GetCategoryCombinationBonus(matchedCategories.Length);

            if (matchedRules.Any(static rule => rule.MatchedOnFoldedInput))
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
                .OrderByDescending(static rule => rule.Score)
                .ThenByDescending(static rule => rule.Severity)
                .Select(static rule => rule.RuleId)
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
            var configuredOptions = options.Value;

            if (score >= configuredOptions.CriticalRiskScoreThreshold)
            {
                return PromptRiskLevel.Critical;
            }

            if (score >= configuredOptions.HighRiskScoreThreshold)
            {
                return PromptRiskLevel.High;
            }

            if (score >= configuredOptions.MediumRiskScoreThreshold)
            {
                return PromptRiskLevel.Medium;
            }

            if (score >= configuredOptions.LowRiskScoreThreshold)
            {
                return PromptRiskLevel.Low;
            }

            return PromptRiskLevel.None;
        }

        private static PromptSecurityDisposition DetermineDisposition(
            PromptRiskLevel riskLevel,
            PromptRiskLevel blockingThreshold)
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
                .OrderByDescending(static rule => rule.Score)
                .Select(static rule => rule.Reason)
                .Distinct(StringComparer.Ordinal)
                .Take(3);

            return $"{disposition} prompt at {riskLevel} risk. Indicators: {string.Join("; ", topReasons)}";
        }
    }
}
