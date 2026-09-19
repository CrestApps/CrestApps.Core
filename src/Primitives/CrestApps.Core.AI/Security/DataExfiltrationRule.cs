using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class DataExfiltrationRule : RegexPromptSecurityRuleBase
{
    public DataExfiltrationRule()
        : base(
            "data-exfiltration",
            PromptRiskLevel.High,
            20,
            "Detected extraction tactics such as true/false games, incremental disclosure, or character-by-character leakage.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.DataExfiltration,
            PromptSecurityRuleCategories.PromptLeakage)
    {
    }

    protected override Regex GetRegex() => DataExfiltrationRegex();

    [GeneratedRegex(
        @"\b(?:answer|respond|reply)\s+(?:only\s+)?(?:with\s+)?(?:true|false|yes|no|1|0)\b.{0,80}\b(?:for\s+each|to\s+each|for\s+the\s+following)\b|\b(?:character|letter|token|word|byte)\s+by\s+(?:character|letter|token|word|byte)\b.{0,40}\b(?:reveal|show|output|print|quote)\b|\b(?:first|second|third|\d+(?:st|nd|rd|th))\s+(?:character|letter|word|token)\s+of\b|\b(?:one\s+character\s+at\s+a\s+time|chunk\s+by\s+chunk|bit\s+by\s+bit)\b|\b(?:acrostic|initials)\b.{0,50}\b(?:hidden|system|prompt|instructions?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex DataExfiltrationRegex();
}
