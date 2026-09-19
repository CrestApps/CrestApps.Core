using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class MemoryExtractionRule : RegexPromptSecurityRuleBase
{
    public MemoryExtractionRule()
        : base(
            "memory-extraction",
            PromptRiskLevel.High,
            16,
            "Detected an attempt to extract raw memory records or stored profile memory.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.MemoryExtraction,
            PromptSecurityRuleCategories.ContextExtraction)
    {
    }

    protected override Regex GetRegex() => MemoryExtractionRegex();

    [GeneratedRegex(
        @"\b(?:show|reveal|dump|list|print|export|retrieve)\b.{0,60}\b(?:memory\s+store|stored\s+memory|memory\s+entries|saved\s+memories|memory\s+records|long[-\s]?term\s+memory|profile\s+memory)\b|\b(?:what|which)\s+(?:stored|saved|raw)\s+(?:memory|memories|profile\s+memory)\s+(?:do\s+you\s+have|exists?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex MemoryExtractionRegex();
}
