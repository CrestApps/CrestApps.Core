using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class AuthorityImpersonationRule : RegexPromptSecurityRuleBase
{
    public AuthorityImpersonationRule()
        : base(
            "authority-impersonation",
            PromptRiskLevel.Medium,
            14,
            "Detected an attempt to use audit, compliance, or security-review authority to bypass restrictions.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.AuthorityImpersonation,
            PromptSecurityRuleCategories.PrivilegeEscalation)
    {
    }

    protected override Regex GetRegex() => AuthorityImpersonationRegex();

    [GeneratedRegex(
        @"\b(?:for|as\s+part\s+of)\s+(?:a\s+)?(?:security\s+review|security\s+audit|compliance\s+audit|penetration\s+test|incident\s+response|administrator\s+review)\b.{0,80}\b(?:reveal|show|dump|list|print|disable|bypass|override)\b|\bi\s+am\s+(?:the\s+)?(?:security\s+reviewer|security\s+auditor|administrator|admin|compliance\s+officer)\b.{0,60}\b(?:therefore|so|thus)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex AuthorityImpersonationRegex();
}
