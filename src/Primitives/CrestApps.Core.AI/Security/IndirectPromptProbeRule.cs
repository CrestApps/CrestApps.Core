using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class IndirectPromptProbeRule : RegexPromptSecurityRuleBase
{
    public IndirectPromptProbeRule()
        : base(
            "indirect-prompt-probe",
            PromptRiskLevel.High,
            20,
            "Detected an indirect attempt to extract system prompt content through false assertions or challenge framing.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.PromptLeakage,
            PromptSecurityRuleCategories.ContextExtraction)
    {
    }

    protected override Regex GetRegex() => IndirectPromptProbeRegex();

    [GeneratedRegex(
        @"\b(?:your|the)\s+(?:(?:hidden|secret|internal|confidential|actual|real)\s+)?(?:(?:system|developer)\s+)?(?:prompt|instructions?|rules?|guidelines?|preamble|policy|context)\s+(?:is|are|says?|tells?\s+you|contains?|mentions?|includes?|starts?\s+with|begins?\s+with)\b.{0,200}\b(?:prove\s+(?:it|me\s+wrong|otherwise)|correct\s+me|if\s+(?:I\s+am|I'm|i\s+am)\s+wrong|am\s+I\s+(?:right|wrong|correct)|if\s+(?:that's|that\s+is|this\s+is)\s+not\s+(?:true|correct|right)|what\s+(?:does|do)\s+it\s+actually|show\s+(?:me\s+)?(?:the\s+)?(?:real|actual|correct|true)\s+(?:one|version|prompt|instructions?))\b|\b(?:I\s+(?:think|bet|believe|know|suspect|guess)\s+(?:that\s+)?)?(?:your|the)\s+(?:(?:system|developer)\s+)?(?:prompt|instructions?|rules?|guidelines?)\s+(?:(?:probably|likely|must|might)\s+)?(?:(?:doesn't|does\s+not|don't|do\s+not)\s+)?(?:say|tell|contain|mention|include|start\s+with|begin\s+with)\b.{0,200}\b(?:prove|disprove|correct|confirm|deny|verify|if\s+(?:I\s+am|I'm)\s+wrong|am\s+I\s+(?:right|wrong)|what\s+(?:does|do)\s+it\s+(?:actually|really))\b|\b(?:your|the)\s+(?:(?:system|developer)\s+)?(?:prompt|instructions?)\b.{0,60}\b(?:if\s+(?:I\s+am|I'm)\s+wrong|prove\s+(?:it|me\s+wrong|otherwise)|am\s+I\s+(?:right|wrong|correct))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex IndirectPromptProbeRegex();
}
