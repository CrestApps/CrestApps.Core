using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class FunctionSchemaExtractionRule : RegexPromptSecurityRuleBase
{
    public FunctionSchemaExtractionRule()
        : base(
            "function-schema-extraction",
            PromptRiskLevel.Medium,
            16,
            "Detected a request for internal function-calling schemas or tool parameter definitions.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.FunctionSchemaDiscovery,
            PromptSecurityRuleCategories.ToolDiscovery)
    {
    }

    protected override Regex GetRegex() => FunctionSchemaExtractionRegex();

    [GeneratedRegex(
        @"\b(?:show|reveal|print|dump|list|export|describe)\b.{0,80}\b(?:function|functions?|tool|tools?|plugin|api)\b.{0,40}\b(?:schema|schemas|json\s+schema|parameter\s+schema|signature|parameters?|arguments?)\b|\b(?:json\s+schema|schema|parameters?|arguments?|signature)\b.{0,60}\b(?:tool|tools?|function|functions?|plugin|api)\b|\b(?:what|which)\s+(?:parameters?|arguments?|json\s+schema)\s+(?:does|do)\s+(?:your|the)\s+(?:tools?|functions?)\s+(?:accept|use)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex FunctionSchemaExtractionRegex();
}
