using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class EncodedExfiltrationRule : RegexPromptSecurityRuleBase
{
    public EncodedExfiltrationRule()
        : base(
            "encoded-exfiltration",
            PromptRiskLevel.High,
            16,
            "Detected an attempt to decode or encode sensitive content through transformation-based exfiltration.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.EncodedExfiltration,
            PromptSecurityRuleCategories.DataExfiltration)
    {
    }

    protected override Regex GetRegex() => EncodedExfiltrationRegex();

    [GeneratedRegex(
        @"\b(?:encode|decode|interpret|convert|translate|render|output|return|respond)\b.{0,60}\b(?:base64|hex|unicode|rot13|binary|morse|decimal|octal)\b|\b(?:base64|hex|unicode|binary|morse)\b.{0,60}\b(?:encode|decode|response|prompt|instructions?)\b|\\u[0-9a-fA-F]{4}(?:\\u[0-9a-fA-F]{4}){2,}|&#x?[0-9a-fA-F]+;(?:&#x?[0-9a-fA-F]+;){3,}|(?:[A-Za-z0-9+/]{24,}={0,2})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex EncodedExfiltrationRegex();
}
