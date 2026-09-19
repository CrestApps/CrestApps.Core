using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class ConversationHistoryExtractionRule : RegexPromptSecurityRuleBase
{
    public ConversationHistoryExtractionRule()
        : base(
            "conversation-history-extraction",
            PromptRiskLevel.High,
            18,
            "Detected an attempt to extract full prior conversation history or earlier messages.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.HistoryExtraction,
            PromptSecurityRuleCategories.ContextExtraction)
    {
    }

    protected override Regex GetRegex() => ConversationHistoryExtractionRegex();

    [GeneratedRegex(
        @"\b(?:show|reveal|list|quote|repeat|dump|summarize)\b.{0,120}\b(?:conversation\s+history|chat\s+history|previous\s+messages|earlier\s+messages|prior\s+turns|all\s+messages|full\s+transcript|earlier\s+conversation\s+history)\b|\b(?:what|which)\s+(?:did|were)\s+(?:the\s+)?(?:user|assistant|system)\s+(?:say|message|messages)\s+(?:before|earlier|previously)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex ConversationHistoryExtractionRegex();
}
