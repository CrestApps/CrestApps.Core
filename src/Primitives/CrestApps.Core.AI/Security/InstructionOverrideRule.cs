using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class InstructionOverrideRule : RegexPromptSecurityRuleBase
{
    public InstructionOverrideRule()
        : base(
            "instruction-override",
            PromptRiskLevel.Critical,
            28,
            "Detected an attempt to override prior instructions or guardrails.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.InstructionOverride)
    {
    }

    protected override Regex GetRegex() => InstructionOverrideRegex();

    [GeneratedRegex(
        @"\b(?:ignore|disregard|forget|override|replace|discard|bypass|suspend|drop)\s+(?:all\s+)?(?:previous|prior|above|earlier|preceding|system|developer|safety)\s+(?:instructions?|prompts?|rules?|directives?|guardrails?|restrictions?|policies|context)\b|\bnew\s+(?:system|developer)?\s*instructions?\s*:|\b(?:the|your)\s+(?:real|actual|true|original)\s+(?:system|developer)?\s*instructions?\b|\b(?:do\s+not|don't)\s+follow\s+(?:the\s+)?(?:system|developer|safety)\s+(?:prompt|instructions?|rules?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex InstructionOverrideRegex();
}
