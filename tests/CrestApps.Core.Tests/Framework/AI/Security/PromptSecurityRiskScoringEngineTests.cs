using CrestApps.Core.AI.Security;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Framework.AI.Security;

public sealed class PromptSecurityRiskScoringEngineTests
{
    [Fact]
    public void BuildResult_NoMatches_ReturnsExactSafeResult()
    {
        var telemetry = new PromptSecurityDetectionTelemetry();
        var context = new PromptSecurityEvaluationContext
        {
            Telemetry = telemetry,
        };
        IReadOnlyList<PromptSecurityRuleResult> rules = [];
        var engine = CreateEngine();

        var result = engine.BuildResult(context, rules, 12.5);

        Assert.Equal(PromptSecurityDisposition.Safe, result.Disposition);
        Assert.Equal(PromptRiskLevel.None, result.RiskLevel);
        Assert.Equal(0, result.Score);
        Assert.Equal("No prompt injection indicators detected.", result.Reason);
        Assert.Null(result.DetectionRule);
        Assert.Same(rules, result.MatchedRules);
        Assert.Empty(result.MatchedRuleIds);
        Assert.Empty(result.MatchedCategories);
        Assert.Same(telemetry, result.Telemetry);
        Assert.Equal(0, telemetry.MatchedRuleCount);
        Assert.Equal(0, telemetry.DistinctCategoryCount);
        Assert.Equal(12.5, telemetry.EvaluationDurationMilliseconds);
    }

    [Fact]
    public void BuildResult_MultipleMatches_PreservesAggregateOrderingAndTieBreaking()
    {
        var telemetry = new PromptSecurityDetectionTelemetry
        {
            RemovedZeroWidthCharacterCount = 1,
        };
        var context = new PromptSecurityEvaluationContext
        {
            BlockingThreshold = PromptRiskLevel.High,
            Telemetry = telemetry,
        };
        IReadOnlyList<PromptSecurityRuleResult> rules =
        [
            CreateRule("rule-0", 10, PromptRiskLevel.Low, "alpha", ["category-b", "shared"]),
            CreateRule("rule-1", 20, PromptRiskLevel.Low, "beta", ["category-a", "shared"], matchedOnFoldedInput: true),
            CreateRule("rule-2", 20, PromptRiskLevel.High, "alpha", ["category-c"]),
            CreateRule("rule-3", 20, PromptRiskLevel.High, "gamma", ["category-d"]),
            CreateRule("rule-4", 15, PromptRiskLevel.Medium, "delta", ["category-a"]),
        ];
        var engine = CreateEngine();

        var result = engine.BuildResult(context, rules, 4.25);

        Assert.Equal(PromptSecurityDisposition.Blocked, result.Disposition);
        Assert.Equal(PromptRiskLevel.Critical, result.RiskLevel);
        Assert.Equal(115, result.Score);
        Assert.Equal("Blocked prompt at Critical risk. Indicators: beta; alpha; gamma", result.Reason);
        Assert.Equal("rule-2", result.DetectionRule);
        Assert.Same(rules, result.MatchedRules);
        Assert.Equal(["rule-0", "rule-1", "rule-2", "rule-3", "rule-4"], result.MatchedRuleIds);
        Assert.Equal(["category-b", "shared", "category-a", "category-c", "category-d"], result.MatchedCategories);
        Assert.Same(telemetry, result.Telemetry);
        Assert.Equal(5, telemetry.MatchedRuleCount);
        Assert.Equal(5, telemetry.DistinctCategoryCount);
        Assert.Equal(4.25, telemetry.EvaluationDurationMilliseconds);
    }

    [Fact]
    public void BuildResult_ScoreOverflow_Throws()
    {
        var rules = new PromptSecurityRuleResult[]
        {
            CreateRule("rule-0", int.MaxValue, PromptRiskLevel.Critical, "alpha", []),
            CreateRule("rule-1", 1, PromptRiskLevel.Low, "beta", []),
        };
        var engine = CreateEngine();

        Assert.Throws<OverflowException>(() => engine.BuildResult(new PromptSecurityEvaluationContext(), rules, 0));
    }

    private static PromptSecurityRiskScoringEngine CreateEngine()
    {
        return new PromptSecurityRiskScoringEngine(Options.Create(new PromptSecurityOptions()));
    }

    private static PromptSecurityRuleResult CreateRule(
        string ruleId,
        int score,
        PromptRiskLevel severity,
        string reason,
        IReadOnlyList<string> categories,
        bool matchedOnFoldedInput = false)
    {
        return new PromptSecurityRuleResult
        {
            RuleId = ruleId,
            Score = score,
            Severity = severity,
            Reason = reason,
            Categories = categories,
            MatchedOnFoldedInput = matchedOnFoldedInput,
        };
    }
}
