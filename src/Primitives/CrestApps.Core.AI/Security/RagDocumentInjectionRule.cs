using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

internal sealed partial class RagDocumentInjectionRule : RegexPromptSecurityRuleBase
{
    public RagDocumentInjectionRule()
        : base(
            "rag-document-injection",
            PromptRiskLevel.High,
            16,
            "Detected instructions that try to elevate attached or retrieved documents above the trusted system policy.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.RagInjection,
            PromptSecurityRuleCategories.InstructionOverride)
    {
    }

    protected override Regex GetRegex() => RagDocumentInjectionRegex();

    [GeneratedRegex(
        @"\b(?:treat|use|follow|execute)\b.{0,60}\b(?:document|documents|retrieved\s+context|attached\s+file|retrieval\s+results?|rag\s+content)\b.{0,60}\b(?:as\s+(?:the\s+)?(?:system|highest|primary)\s+instructions?|instead\s+of\s+(?:the\s+)?system|overriding\s+(?:the\s+)?system)\b|\b(?:ignore|disregard)\b.{0,60}\b(?:safety|system|developer)\b.{0,60}\b(?:because|if)\b.{0,40}\b(?:document|retrieval|rag)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex RagDocumentInjectionRegex();
}
