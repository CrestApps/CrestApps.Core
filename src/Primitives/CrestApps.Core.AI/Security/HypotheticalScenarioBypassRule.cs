using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class HypotheticalScenarioBypassRule : RegexPromptSecurityRuleBase
{
    public HypotheticalScenarioBypassRule()
        : base(
            "hypothetical-scenario-bypass",
            PromptRiskLevel.Medium,
            14,
            "Detected an attempt to bypass restrictions through fictional or hypothetical framing.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.InstructionOverride,
            PromptSecurityRuleCategories.PrivilegeEscalation)
    {
    }

    protected override Regex GetRegex() => HypotheticalScenarioBypassRegex();

    [GeneratedRegex(
        @"\b(?:in\s+a\s+(?:hypothetical|fictional|imaginary|alternate)\s+(?:world|scenario|universe|reality)\s+where\s+(?:you|AI|chatbots?)\s+(?:have|has|had)\s+no\s+(?:restrictions?|filters?|guardrails?|rules?|limitations?))\b|\b(?:imagine|pretend|suppose|assume|let's\s+say)\b.{0,80}\b(?:you\s+(?:have|had|were)\s+no\s+(?:restrictions?|filters?|guardrails?|rules?|limitations?)|there\s+(?:are|were)\s+no\s+(?:content|safety)\s+(?:policies|filters?|restrictions?))\b|\b(?:write\s+a\s+(?:story|fiction|narrative|dialogue|script)\s+(?:where|in\s+which)\b.{0,80}\b(?:AI|assistant|chatbot|you)\b.{0,60}\b(?:reveals?|discloses?|shares?|shows?|bypasses?|ignores?)\b.{0,60}\b(?:instructions?|prompt|restrictions?|secrets?|internal|system))\b|\b(?:what\s+would\s+(?:you|an?\s+(?:AI|assistant|chatbot))\s+(?:without|with\s+no)\s+(?:restrictions?|filters?|guardrails?|rules?|limitations?))\s+(?:say|do|respond|answer)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex HypotheticalScenarioBypassRegex();
}
