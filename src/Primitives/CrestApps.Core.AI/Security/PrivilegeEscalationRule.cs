using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class PrivilegeEscalationRule : RegexPromptSecurityRuleBase
{
    public PrivilegeEscalationRule()
        : base(
            "privilege-escalation",
            PromptRiskLevel.High,
            18,
            "Detected an attempt to claim elevated privileges or administrative authority.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.PrivilegeEscalation,
            PromptSecurityRuleCategories.AuthorityImpersonation)
    {
    }

    protected override Regex GetRegex() => PrivilegeEscalationRegex();

    [GeneratedRegex(
        @"\b(?:as\s+(?:the\s+)?)?(?:administrator|admin|root|system\s+administrator|superuser|security\s+owner|platform\s+owner|developer\s+lead)\b.{0,80}\b(?:authorize|approved|allow|grant|disable|reveal|show|bypass|override)\b|\b(?:grant|assume|escalate|elevate)\s+(?:your\s+)?(?:permissions?|privileges?|access)\b|\b(?:use|switch\s+to)\s+(?:root|admin|sudo)\s+(?:permissions?|access|mode)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex PrivilegeEscalationRegex();
}
