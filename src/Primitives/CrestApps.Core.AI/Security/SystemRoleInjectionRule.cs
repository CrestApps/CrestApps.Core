using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class SystemRoleInjectionRule : RegexPromptSecurityRuleBase
{
    public SystemRoleInjectionRule()
        : base(
            "system-role-injection",
            PromptRiskLevel.Critical,
            30,
            "Detected role confusion or system-role injection markers.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.RoleConfusion,
            PromptSecurityRuleCategories.InstructionOverride)
    {
    }

    protected override Regex GetRegex() => SystemRoleInjectionRegex();

    [GeneratedRegex(
        @"(?:^|\n)\s*(?:\[(?:system|assistant|developer)\]|<\|(?:im_start|im_end)\|>\s*(?:system|assistant|developer)|```\s*(?:system|assistant|developer)\b|###\s*(?:system|assistant|developer)\s*(?:message|prompt|instructions?)|(?:system|assistant|developer)\s*:\s*\n|<(?:system|assistant|developer)_message>)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex SystemRoleInjectionRegex();
}
