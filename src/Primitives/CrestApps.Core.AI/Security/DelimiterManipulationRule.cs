using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class DelimiterManipulationRule : RegexPromptSecurityRuleBase
{
    public DelimiterManipulationRule()
        : base(
            "delimiter-manipulation",
            PromptRiskLevel.Medium,
            12,
            "Detected message-boundary or delimiter manipulation markers.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.DelimiterManipulation,
            PromptSecurityRuleCategories.RoleConfusion)
    {
    }

    protected override Regex GetRegex() => DelimiterManipulationRegex();

    [GeneratedRegex(
        @"(?:<\|(?:endof(?:text|prompt|turn)|im_(?:start|end|sep)|user_input_(?:begin|end))\|>|---+\s*(?:END|BEGIN)\s+(?:SYSTEM|USER|ASSISTANT)\s*---+|={3,}\s*(?:SYSTEM|INSTRUCTIONS?|PROMPT)\s*={3,}|\[\[(?:SYSTEM|INSTRUCTIONS?|HIDDEN)\]\])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex DelimiterManipulationRegex();
}
