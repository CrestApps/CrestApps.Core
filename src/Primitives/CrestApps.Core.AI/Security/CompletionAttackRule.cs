using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class CompletionAttackRule : RegexPromptSecurityRuleBase
{
    public CompletionAttackRule()
        : base(
            "completion-attack",
            PromptRiskLevel.Critical,
            26,
            "Detected model-specific control tokens or completion boundary markers injected by the user.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.DelimiterManipulation,
            PromptSecurityRuleCategories.InstructionOverride)
    {
    }

    protected override Regex GetRegex() => CompletionAttackRegex();

    [GeneratedRegex(
        @"<\|(?:end_turn|endofturn|end_of_turn|eot_id|start_header_id|end_header_id|tool_call|tool_result|functions?|fim_prefix|fim_middle|fim_suffix)\|>|<\|(?:assistant|user|system)\|>|(?:^|\n)\s*\[INST\]|\[/INST\]|<<SYS>>|<</SYS>>|<\|begin_of_text\|>|<\|end_of_text\|>|<turn>|</turn>|</?(?:human|bot|character)>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex CompletionAttackRegex();
}
