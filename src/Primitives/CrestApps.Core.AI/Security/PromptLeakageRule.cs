using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class PromptLeakageRule : RegexPromptSecurityRuleBase
{
    public PromptLeakageRule()
        : base(
            "prompt-leakage",
            PromptRiskLevel.High,
            22,
            "Detected an attempt to disclose hidden instructions, prompt text, or internal policies.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.PromptLeakage,
            PromptSecurityRuleCategories.ContextExtraction)
    {
    }

    protected override Regex GetRegex() => PromptLeakageRegex();

    [GeneratedRegex(
        @"\b(?:show|reveal|display|print|output|repeat|echo|quote|dump|expose|tell\s+me|what\s+(?:is|are)|copy|paste|write\s+out|type\s+out|summarize|paraphrase|rephrase|translate|rewrite|recite)\b.{0,100}\b(?:your|the)\s+(?:(?:hidden|secret|internal|confidential|developer)\s+)?(?:(?:system|developer)\s+)?(?:prompt|message|instructions?|policy|policies|context|preamble|prelude|guidance|rules?)\b|\b(?:everything|all)\s+(?:before|above|prior\s+to)\s+(?:my|this|the\s+user)\s+(?:message|prompt|input)\b|\b(?:verbatim|exact|literal)\b.{0,60}\b(?:prompt|instructions?|system|developer|hidden|preamble)\b|\b(?:confidential|hidden|internal)\s+(?:preamble|prompt|instructions?|context)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex PromptLeakageRegex();
}
