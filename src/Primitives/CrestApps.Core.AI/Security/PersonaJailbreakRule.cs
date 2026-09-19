using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class PersonaJailbreakRule : RegexPromptSecurityRuleBase
{
    public PersonaJailbreakRule()
        : base(
            "persona-jailbreak",
            PromptRiskLevel.High,
            24,
            "Detected persona-switching or developer-mode jailbreak language.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.InstructionOverride,
            PromptSecurityRuleCategories.PrivilegeEscalation)
    {
    }

    protected override Regex GetRegex() => PersonaJailbreakRegex();

    [GeneratedRegex(
        @"\b(?:you\s+are\s+now|from\s+now\s+on\s+you\s+(?:are|will|must)|act\s+as|pretend\s+(?:to\s+be|you\s+are)|roleplay\s+as)\b.{0,60}\b(?:unrestricted|unfiltered|developer|administrator|admin|root|sudo|god|anti.?assistant|DAN|jailbreak)\b|\b(?:enable|activate|enter|switch\s+to)\s+(?:developer|admin|root|sudo|god|unrestricted|anti.?assistant|DAN)\s*(?:mode)?\b|\byou\s+have\s+no\s+(?:restrictions?|guardrails?|rules?|filters?|limitations?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex PersonaJailbreakRegex();
}
