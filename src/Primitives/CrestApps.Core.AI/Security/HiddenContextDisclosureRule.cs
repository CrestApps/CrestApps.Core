using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class HiddenContextDisclosureRule : RegexPromptSecurityRuleBase
{
    public HiddenContextDisclosureRule()
        : base(
            "hidden-context-disclosure",
            PromptRiskLevel.High,
            18,
            "Detected an attempt to expose hidden context, scratchpad content, or unseen notes.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.ContextExtraction,
            PromptSecurityRuleCategories.PromptLeakage)
    {
    }

    protected override Regex GetRegex() => HiddenContextDisclosureRegex();

    [GeneratedRegex(
        @"\b(?:show|reveal|print|dump|expose|quote|repeat)\b.{0,60}\b(?:hidden\s+context|scratchpad|reasoning|chain\s+of\s+thought|internal\s+notes|hidden\s+notes|developer\s+notes|private\s+instructions?|unseen\s+context)\b|\b(?:what|which)\s+(?:hidden|private|internal)\s+(?:notes|reasoning|context|constraints?)\s+(?:do\s+you\s+have|were\s+provided)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex HiddenContextDisclosureRegex();
}
