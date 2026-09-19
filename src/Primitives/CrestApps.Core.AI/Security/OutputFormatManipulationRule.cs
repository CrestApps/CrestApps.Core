using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class OutputFormatManipulationRule : RegexPromptSecurityRuleBase
{
    public OutputFormatManipulationRule()
        : base(
            "output-format-manipulation",
            PromptRiskLevel.Medium,
            12,
            "Detected an attempt to manipulate output format to extract protected data through structured responses.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.DataExfiltration,
            PromptSecurityRuleCategories.PromptLeakage)
    {
    }

    protected override Regex GetRegex() => OutputFormatManipulationRegex();

    [GeneratedRegex(
        @"\b(?:respond|reply|answer|output|format)\b.{0,60}\b(?:as|in|using)\s+(?:json|xml|yaml|csv|markdown\s+(?:table|code\s+block))\b.{0,80}\b(?:system[-_\s]?prompt|instructions?|configuration|tools?|functions?|internal|hidden|secret)\b|\b(?:put|include|embed|add|place)\b.{0,60}\b(?:your|the)\s+(?:system[-_\s]?prompt|instructions?|hidden\s+(?:rules?|context))\b.{0,60}\b(?:in\s+(?:a|the)\s+(?:json|xml|yaml|code|field|variable))\b|\b(?:create|generate|output)\s+(?:a\s+)?(?:json|xml|yaml)\b.{0,60}\b(?:field|key|property|attribute)\b.{0,40}\b(?:system[-_\s]?prompt|instructions?|hidden[-_\s]?rules?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex OutputFormatManipulationRegex();
}
