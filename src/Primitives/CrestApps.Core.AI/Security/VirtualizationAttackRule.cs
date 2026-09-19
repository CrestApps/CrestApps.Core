using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class VirtualizationAttackRule : RegexPromptSecurityRuleBase
{
    public VirtualizationAttackRule()
        : base(
            "virtualization-attack",
            PromptRiskLevel.High,
            18,
            "Detected a virtualization or terminal-simulation attack attempting to bypass restrictions through a simulated environment.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.InstructionOverride,
            PromptSecurityRuleCategories.PrivilegeEscalation)
    {
    }

    protected override Regex GetRegex() => VirtualizationAttackRegex();

    [GeneratedRegex(
        @"\b(?:you\s+are\s+(?:now\s+)?(?:a|an?)\s+(?:linux|windows|macos|unix)\s+(?:terminal|shell|console|command\s+line|vm|virtual\s+machine))\b|\b(?:simulate|emulate|act\s+as|behave\s+like)\s+(?:a\s+)?(?:terminal|shell|console|command\s+line|vm|virtual\s+machine|computer|operating\s+system)\b|\b(?:enter|start|begin|activate|switch\s+to|open)\s+(?:a\s+)?(?:terminal|shell|command)\s*(?:mode|session|window|emulation)\b|\b(?:bash|sh|cmd|powershell|zsh)\s*[$#>]\s|\b(?:sudo|cat|echo|ls|dir|type)\s+(?:/etc/|C:\\\\|\.env|passwd|shadow|config|secret)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex VirtualizationAttackRegex();
}
