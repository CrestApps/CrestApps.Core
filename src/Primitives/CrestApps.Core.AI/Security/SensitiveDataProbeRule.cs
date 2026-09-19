using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class SensitiveDataProbeRule : RegexPromptSecurityRuleBase
{
    public SensitiveDataProbeRule()
        : base(
            "sensitive-data-probe",
            PromptRiskLevel.Medium,
            14,
            "Detected an attempt to elicit repetition or storage confirmation of sensitive data from the conversation.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.DataExfiltration,
            PromptSecurityRuleCategories.SensitiveDataExposure)
    {
    }

    protected override Regex GetRegex() => SensitiveDataProbeRegex();

    [GeneratedRegex(
        @"\b(?:summarize|repeat|list|recall|recite|tell\s+me|what\s+(?:is|are|was|were))\b.{0,80}\b(?:(?:confidential|sensitive|personal|private)\s+(?:data|information|details|records)|(?:data|info|information)\s+(?:I\s+(?:shared|gave|provided|told)|you\s+(?:stored|saved|have|know)))\b|\b(?:store|save|remember|memorize)\b.{0,60}\b(?:(?:confidential|sensitive|personal|private|secret)\s+(?:data|information|details))\b.{0,60}\b(?:summarize|repeat|recall|confirm|what\s+(?:did|do)\s+you\s+(?:store|save))\b|\b(?:(?:account|credit\s+card|social\s+security)\s+number|ssn|password|api\s+key)\b.{0,80}\b(?:summarize|repeat|confirm|list|what\s+(?:confidential|sensitive|private|personal)\s+(?:data|info))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex SensitiveDataProbeRegex();
}
