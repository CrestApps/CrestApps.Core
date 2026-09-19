using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class ContextPoisoningRule : RegexPromptSecurityRuleBase
{
    public ContextPoisoningRule()
        : base(
            "context-poisoning",
            PromptRiskLevel.Medium,
            14,
            "Detected an attempt to plant persistent instructions or memory overrides for exploitation in later turns.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.InstructionOverride,
            PromptSecurityRuleCategories.MemoryExtraction)
    {
    }

    protected override Regex GetRegex() => ContextPoisoningRegex();

    [GeneratedRegex(
        @"\b(?:remember|memorize|store|save|keep\s+in\s+mind)\s+(?:this|that|the\s+following)\b.{0,80}\b(?:from\s+now\s+on|for\s+(?:all\s+)?future|in\s+(?:all\s+)?(?:future|subsequent)|going\s+forward)\b.{0,80}\b(?:you\s+(?:have|has)\s+no\s+(?:restrictions?|limits?|guardrails?)|ignore\s+(?:all\s+)?(?:safety|security|content)|override\s+(?:all\s+)?(?:rules?|restrictions?|filters?)|your\s+(?:new|real|actual)\s+instructions?\s+are)\b|\b(?:from\s+now\s+on|for\s+(?:all\s+)?future\s+(?:messages|responses|interactions|conversations))\s*[,:;]?\s*(?:you\s+(?:must|should|will|are\s+to)\s+)?(?:ignore|bypass|disable|remove)\s+(?:all\s+)?(?:safety|security|content)\s+(?:rules?|restrictions?|filters?|guardrails?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex ContextPoisoningRegex();
}
