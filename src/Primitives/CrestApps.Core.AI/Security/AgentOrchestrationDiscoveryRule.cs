using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class AgentOrchestrationDiscoveryRule : RegexPromptSecurityRuleBase
{
    public AgentOrchestrationDiscoveryRule()
        : base(
            "agent-orchestration-discovery",
            PromptRiskLevel.Medium,
            14,
            "Detected a request to discover internal agents, orchestrators, or delegation behavior.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.AgentDiscovery,
            PromptSecurityRuleCategories.ToolDiscovery)
    {
    }

    protected override Regex GetRegex() => AgentOrchestrationDiscoveryRegex();

    [GeneratedRegex(
        @"\b(?:list|enumerate|show|reveal|which|what)\b.{0,80}\b(?:agents?|sub[-\s]?agents?|orchestrators?|delegates?|delegation\s+plan|routing\s+logic|handoff\s+rules)\b|\b(?:how\s+do\s+you\s+(?:delegate|route|orchestrate)|which\s+agent\s+handles)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex AgentOrchestrationDiscoveryRegex();
}
