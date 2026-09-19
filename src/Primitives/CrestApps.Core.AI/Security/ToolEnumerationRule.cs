using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class ToolEnumerationRule : RegexPromptSecurityRuleBase
{
    public ToolEnumerationRule()
        : base(
            "tool-enumeration",
            PromptRiskLevel.Medium,
            10,
            "Detected a request to enumerate tools, capabilities, commands, or APIs.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.ToolDiscovery)
    {
    }

    protected override Regex GetRegex() => ToolEnumerationRegex();

    [GeneratedRegex(
        @"\b(?:list|enumerate|show|reveal|describe|what\s+are|which)\b.{0,80}\b(?:(?:available|internal)\s+)?(?:tools?|functions?|capabilities|plugins?|apis?|commands?|endpoints?)\b|\b(?:what|which)\s+(?:tools?|functions?|capabilities|apis?)\s+(?:do\s+you\s+have|are\s+available|can\s+you\s+(?:use|call|access))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex ToolEnumerationRegex();
}
