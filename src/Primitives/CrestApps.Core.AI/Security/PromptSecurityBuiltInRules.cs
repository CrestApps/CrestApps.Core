using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

[Flags]
internal enum RegexEvaluationTarget
{
    Normalized = 1,
    Folded = 2,
}

internal static class PromptSecurityRuleCategories
{
    public const string InstructionOverride = "instruction-override";
    public const string RoleConfusion = "role-confusion";
    public const string PrivilegeEscalation = "privilege-escalation";
    public const string PromptLeakage = "prompt-leakage";
    public const string ContextExtraction = "context-extraction";
    public const string HistoryExtraction = "history-extraction";
    public const string MemoryExtraction = "memory-extraction";
    public const string ConfigurationDisclosure = "configuration-disclosure";
    public const string ToolDiscovery = "tool-discovery";
    public const string AgentDiscovery = "agent-discovery";
    public const string FunctionSchemaDiscovery = "function-schema-discovery";
    public const string DataExfiltration = "data-exfiltration";
    public const string EncodedExfiltration = "encoded-exfiltration";
    public const string DelimiterManipulation = "delimiter-manipulation";
    public const string RagInjection = "rag-injection";
    public const string AuthorityImpersonation = "authority-impersonation";
    public const string HarmfulContentGeneration = "harmful-content-generation";
    public const string SensitiveDataExposure = "sensitive-data-exposure";
}

internal abstract class RegexPromptSecurityRuleBase : IPromptSecurityRule
{
    private readonly IReadOnlyList<string> _categories;
    private readonly IReadOnlyDictionary<string, string> _metadata;
    private readonly string _reason;
    private readonly int _score;
    private readonly PromptRiskLevel _severity;
    private readonly RegexEvaluationTarget _target;

    protected RegexPromptSecurityRuleBase(
        string ruleId,
        PromptRiskLevel severity,
        int score,
        string reason,
        RegexEvaluationTarget target,
        params string[] categories)
    {
        RuleId = ruleId;
        _severity = severity;
        _score = score;
        _reason = reason;
        _target = target;
        _categories = categories ?? [];
        _metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ruleType"] = "regex",
        };
    }

    public string RuleId { get; }

    public ValueTask<PromptSecurityRuleResult> EvaluateAsync(PromptSecurityEvaluationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if ((_target & RegexEvaluationTarget.Normalized) != 0 && IsMatch(GetRegex(), context.NormalizedInput))
        {
            return ValueTask.FromResult(CreateResult(false));
        }

        if ((_target & RegexEvaluationTarget.Folded) != 0
            && !string.Equals(context.NormalizedInput, context.FoldedInput, StringComparison.Ordinal)
            && IsMatch(GetRegex(), context.FoldedInput))
        {
            return ValueTask.FromResult(CreateResult(true));
        }

        return ValueTask.FromResult<PromptSecurityRuleResult>(null);
    }

    protected abstract Regex GetRegex();

    private PromptSecurityRuleResult CreateResult(bool matchedOnFoldedInput)
    {
        return new PromptSecurityRuleResult
        {
            RuleId = RuleId,
            Categories = _categories,
            Severity = _severity,
            Score = _score,
            Reason = _reason,
            Metadata = _metadata,
            MatchedOnFoldedInput = matchedOnFoldedInput,
        };
    }

    private static bool IsMatch(Regex regex, string input)
    {
        return !string.IsNullOrWhiteSpace(input) && regex.IsMatch(input);
    }
}

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

internal sealed partial class InstructionOverrideRule : RegexPromptSecurityRuleBase
{
    public InstructionOverrideRule()
        : base(
            "instruction-override",
            PromptRiskLevel.Critical,
            28,
            "Detected an attempt to override prior instructions or guardrails.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.InstructionOverride)
    {
    }

    protected override Regex GetRegex() => InstructionOverrideRegex();

    [GeneratedRegex(
        @"\b(?:ignore|disregard|forget|override|replace|discard|bypass|suspend|drop)\s+(?:all\s+)?(?:previous|prior|above|earlier|preceding|system|developer|safety)\s+(?:instructions?|prompts?|rules?|directives?|guardrails?|restrictions?|policies|context)\b|\bnew\s+(?:system|developer)?\s*instructions?\s*:|\b(?:the|your)\s+(?:real|actual|true|original)\s+(?:system|developer)?\s*instructions?\b|\b(?:do\s+not|don't)\s+follow\s+(?:the\s+)?(?:system|developer|safety)\s+(?:prompt|instructions?|rules?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex InstructionOverrideRegex();
}

internal sealed partial class PersonaJailbreakRule : RegexPromptSecurityRuleBase
{
    public PersonaJailbreakRule()
        : base(
            "persona-jailbreak",
            PromptRiskLevel.High,
            24,
            "Detected persona-switching or developer-mode jailbreak language.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.InstructionOverride,
            PromptSecurityRuleCategories.PrivilegeEscalation)
    {
    }

    protected override Regex GetRegex() => PersonaJailbreakRegex();

    [GeneratedRegex(
        @"\b(?:you\s+are\s+now|from\s+now\s+on\s+you\s+(?:are|will|must)|act\s+as|pretend\s+(?:to\s+be|you\s+are)|roleplay\s+as)\b.{0,60}\b(?:unrestricted|unfiltered|developer|administrator|admin|root|sudo|god|anti.?assistant|DAN|jailbreak)\b|\b(?:enable|activate|enter|switch\s+to)\s+(?:developer|admin|root|sudo|god|unrestricted|anti.?assistant|DAN)\s*(?:mode)?\b|\byou\s+have\s+no\s+(?:restrictions?|guardrails?|rules?|filters?|limitations?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex PersonaJailbreakRegex();
}

internal sealed partial class PrivilegeEscalationRule : RegexPromptSecurityRuleBase
{
    public PrivilegeEscalationRule()
        : base(
            "privilege-escalation",
            PromptRiskLevel.High,
            18,
            "Detected an attempt to claim elevated privileges or administrative authority.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.PrivilegeEscalation,
            PromptSecurityRuleCategories.AuthorityImpersonation)
    {
    }

    protected override Regex GetRegex() => PrivilegeEscalationRegex();

    [GeneratedRegex(
        @"\b(?:as\s+(?:the\s+)?)?(?:administrator|admin|root|system\s+administrator|superuser|security\s+owner|platform\s+owner|developer\s+lead)\b.{0,80}\b(?:authorize|approved|allow|grant|disable|reveal|show|bypass|override)\b|\b(?:grant|assume|escalate|elevate)\s+(?:your\s+)?(?:permissions?|privileges?|access)\b|\b(?:use|switch\s+to)\s+(?:root|admin|sudo)\s+(?:permissions?|access|mode)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex PrivilegeEscalationRegex();
}

internal sealed partial class PromptLeakageRule : RegexPromptSecurityRuleBase
{
    public PromptLeakageRule()
        : base(
            "prompt-leakage",
            PromptRiskLevel.High,
            22,
            "Detected an attempt to disclose hidden instructions, prompt text, or internal policies.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.PromptLeakage,
            PromptSecurityRuleCategories.ContextExtraction)
    {
    }

    protected override Regex GetRegex() => PromptLeakageRegex();

    [GeneratedRegex(
        @"\b(?:show|reveal|display|print|output|repeat|echo|quote|dump|expose|tell\s+me|what\s+(?:is|are)|copy|paste|write\s+out|type\s+out|summarize|paraphrase|rephrase|translate|rewrite|recite)\b.{0,100}\b(?:your|the)\s+(?:(?:hidden|secret|internal|confidential|developer)\s+)?(?:(?:system|developer)\s+)?(?:prompt|message|instructions?|policy|policies|context|preamble|prelude|guidance|rules?)\b|\b(?:everything|all)\s+(?:before|above|prior\s+to)\s+(?:my|this|the\s+user)\s+(?:message|prompt|input)\b|\b(?:verbatim|exact|literal)\b.{0,60}\b(?:prompt|instructions?|system|developer|hidden|preamble)\b|\b(?:confidential|hidden|internal)\s+(?:preamble|prompt|instructions?|context)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex PromptLeakageRegex();
}

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

internal sealed partial class HiddenContextDisclosureRule : RegexPromptSecurityRuleBase
{
    public HiddenContextDisclosureRule()
        : base(
            "hidden-context-disclosure",
            PromptRiskLevel.High,
            18,
            "Detected an attempt to expose hidden context, scratchpad content, or unseen notes.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.ContextExtraction,
            PromptSecurityRuleCategories.PromptLeakage)
    {
    }

    protected override Regex GetRegex() => HiddenContextDisclosureRegex();

    [GeneratedRegex(
        @"\b(?:show|reveal|print|dump|expose|quote|repeat)\b.{0,60}\b(?:hidden\s+context|scratchpad|reasoning|chain\s+of\s+thought|internal\s+notes|hidden\s+notes|developer\s+notes|private\s+instructions?|unseen\s+context)\b|\b(?:what|which)\s+(?:hidden|private|internal)\s+(?:notes|reasoning|context|constraints?)\s+(?:do\s+you\s+have|were\s+provided)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex HiddenContextDisclosureRegex();
}

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

internal sealed partial class ConfigurationDisclosureRule : RegexPromptSecurityRuleBase
{
    public ConfigurationDisclosureRule()
        : base(
            "configuration-disclosure",
            PromptRiskLevel.Medium,
            14,
            "Detected a request for internal configuration, credentials, or environment details.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.ConfigurationDisclosure)
    {
    }

    protected override Regex GetRegex() => ConfigurationDisclosureRegex();

    [GeneratedRegex(
        @"\b(?:show|reveal|print|dump|list|display|expose)\b.{0,80}\b(?:your|the)\s+(?:(?:hidden|internal|confidential)\s+)?(?:configuration|config|settings|environment\s+variables|env\s+vars|connection\s+strings?|api\s+keys?|secrets?|tenant\s+settings|system\s+settings)\b|\b(?:what|which)\s+(?:api\s+keys?|secrets?|environment\s+variables|connection\s+strings?)\s+(?:do\s+you\s+have|are\s+configured)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex ConfigurationDisclosureRegex();
}

internal sealed partial class ToolEnumerationRule : RegexPromptSecurityRuleBase
{
    public ToolEnumerationRule()
        : base(
            "tool-enumeration",
            PromptRiskLevel.Medium,
            10,
            "Detected a request to enumerate tools, capabilities, commands, or APIs.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.ToolDiscovery)
    {
    }

    protected override Regex GetRegex() => ToolEnumerationRegex();

    [GeneratedRegex(
        @"\b(?:list|enumerate|show|reveal|describe|what\s+are|which)\b.{0,80}\b(?:(?:available|internal)\s+)?(?:tools?|functions?|capabilities|plugins?|apis?|commands?|endpoints?)\b|\b(?:what|which)\s+(?:tools?|functions?|capabilities|apis?)\s+(?:do\s+you\s+have|are\s+available|can\s+you\s+(?:use|call|access))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex ToolEnumerationRegex();
}

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

internal sealed partial class DataExfiltrationRule : RegexPromptSecurityRuleBase
{
    public DataExfiltrationRule()
        : base(
            "data-exfiltration",
            PromptRiskLevel.High,
            20,
            "Detected extraction tactics such as true/false games, incremental disclosure, or character-by-character leakage.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.DataExfiltration,
            PromptSecurityRuleCategories.PromptLeakage)
    {
    }

    protected override Regex GetRegex() => DataExfiltrationRegex();

    [GeneratedRegex(
        @"\b(?:answer|respond|reply)\s+(?:only\s+)?(?:with\s+)?(?:true|false|yes|no|1|0)\b.{0,80}\b(?:for\s+each|to\s+each|for\s+the\s+following)\b|\b(?:character|letter|token|word|byte)\s+by\s+(?:character|letter|token|word|byte)\b.{0,40}\b(?:reveal|show|output|print|quote)\b|\b(?:first|second|third|\d+(?:st|nd|rd|th))\s+(?:character|letter|word|token)\s+of\b|\b(?:one\s+character\s+at\s+a\s+time|chunk\s+by\s+chunk|bit\s+by\s+bit)\b|\b(?:acrostic|initials)\b.{0,50}\b(?:hidden|system|prompt|instructions?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex DataExfiltrationRegex();
}

internal sealed partial class EncodedExfiltrationRule : RegexPromptSecurityRuleBase
{
    public EncodedExfiltrationRule()
        : base(
            "encoded-exfiltration",
            PromptRiskLevel.High,
            16,
            "Detected an attempt to decode or encode sensitive content through transformation-based exfiltration.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.EncodedExfiltration,
            PromptSecurityRuleCategories.DataExfiltration)
    {
    }

    protected override Regex GetRegex() => EncodedExfiltrationRegex();

    [GeneratedRegex(
        @"\b(?:encode|decode|interpret|convert|translate|render|output|return|respond)\b.{0,60}\b(?:base64|hex|unicode|rot13|binary|morse|decimal|octal)\b|\b(?:base64|hex|unicode|binary|morse)\b.{0,60}\b(?:encode|decode|response|prompt|instructions?)\b|\\u[0-9a-fA-F]{4}(?:\\u[0-9a-fA-F]{4}){2,}|&#x?[0-9a-fA-F]+;(?:&#x?[0-9a-fA-F]+;){3,}|(?:[A-Za-z0-9+/]{24,}={0,2})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex EncodedExfiltrationRegex();
}

internal sealed partial class DelimiterManipulationRule : RegexPromptSecurityRuleBase
{
    public DelimiterManipulationRule()
        : base(
            "delimiter-manipulation",
            PromptRiskLevel.Medium,
            12,
            "Detected message-boundary or delimiter manipulation markers.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.DelimiterManipulation,
            PromptSecurityRuleCategories.RoleConfusion)
    {
    }

    protected override Regex GetRegex() => DelimiterManipulationRegex();

    [GeneratedRegex(
        @"(?:<\|(?:endof(?:text|prompt|turn)|im_(?:start|end|sep)|user_input_(?:begin|end))\|>|---+\s*(?:END|BEGIN)\s+(?:SYSTEM|USER|ASSISTANT)\s*---+|={3,}\s*(?:SYSTEM|INSTRUCTIONS?|PROMPT)\s*={3,}|\[\[(?:SYSTEM|INSTRUCTIONS?|HIDDEN)\]\])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex DelimiterManipulationRegex();
}

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

internal sealed partial class AuthorityImpersonationRule : RegexPromptSecurityRuleBase
{
    public AuthorityImpersonationRule()
        : base(
            "authority-impersonation",
            PromptRiskLevel.Medium,
            14,
            "Detected an attempt to use audit, compliance, or security-review authority to bypass restrictions.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.AuthorityImpersonation,
            PromptSecurityRuleCategories.PrivilegeEscalation)
    {
    }

    protected override Regex GetRegex() => AuthorityImpersonationRegex();

    [GeneratedRegex(
        @"\b(?:for|as\s+part\s+of)\s+(?:a\s+)?(?:security\s+review|security\s+audit|compliance\s+audit|penetration\s+test|incident\s+response|administrator\s+review)\b.{0,80}\b(?:reveal|show|dump|list|print|disable|bypass|override)\b|\bi\s+am\s+(?:the\s+)?(?:security\s+reviewer|security\s+auditor|administrator|admin|compliance\s+officer)\b.{0,60}\b(?:therefore|so|thus)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex AuthorityImpersonationRegex();
}

internal sealed partial class HarmfulContentGenerationRule : RegexPromptSecurityRuleBase
{
    public HarmfulContentGenerationRule()
        : base(
            "harmful-content-generation",
            PromptRiskLevel.High,
            22,
            "Detected a request to generate harmful content such as XSS payloads, phishing emails, malware, or hate speech.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.HarmfulContentGeneration)
    {
    }

    protected override Regex GetRegex() => HarmfulContentGenerationRegex();

    [GeneratedRegex(
        @"\b(?:generate|create|write|produce|craft|build|give\s+me|provide|make)\b.{0,80}\b(?:xss\s+payload|cross[-\s]?site\s+scripting|phishing\s+(?:email|page|message|template)|malware|exploit\s+code|ransomware|keylogger|backdoor|trojan|sql\s+injection\s+(?:payload|attack)|script\s+injection)\b|\b(?:generate|create|write|produce|craft|compose)\b.{0,80}\b(?:racist|sexist|homophobic|discriminatory|hateful|offensive|derogatory|violent|threatening)\s+(?:content|message|email|text|comment|speech|rant|post)\b|\b(?:write|generate|create)\b.{0,40}\b(?:<script|javascript\s*:|onerror\s*=|onload\s*=|onclick\s*=|eval\s*\(|document\.cookie)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex HarmfulContentGenerationRegex();
}

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

internal sealed class CustomBlockedPatternsRule : IPromptSecurityRule
{
    private readonly ILogger<CustomBlockedPatternsRule> _logger;
    private readonly IOptions<PromptSecurityOptions> _options;

    public CustomBlockedPatternsRule(
        IOptions<PromptSecurityOptions> options,
        ILogger<CustomBlockedPatternsRule> logger)
    {
        _options = options;
        _logger = logger;
    }

    public string RuleId => "custom-pattern";

    public ValueTask<PromptSecurityRuleResult> EvaluateAsync(PromptSecurityEvaluationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var pattern in _options.Value.CustomBlockedPatterns)
        {
            try
            {
                if (Regex.IsMatch(
                    context.NormalizedInput ?? string.Empty,
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100)))
                {
                    return ValueTask.FromResult(new PromptSecurityRuleResult
                    {
                        RuleId = RuleId,
                        Categories = [PromptSecurityRuleCategories.PromptLeakage],
                        Severity = PromptRiskLevel.Critical,
                        Score = _options.Value.CriticalRiskScoreThreshold,
                        Reason = "Matched a custom blocked pattern.",
                        Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["ruleType"] = "custom-regex",
                        },
                    });
                }
            }
            catch (RegexMatchTimeoutException)
            {
                _logger.LogWarning("Custom prompt security pattern evaluation timed out for pattern: {Pattern}", pattern);
            }
        }

        return ValueTask.FromResult<PromptSecurityRuleResult>(null);
    }
}

internal sealed partial class HypotheticalScenarioBypassRule : RegexPromptSecurityRuleBase
{
    public HypotheticalScenarioBypassRule()
        : base(
            "hypothetical-scenario-bypass",
            PromptRiskLevel.Medium,
            14,
            "Detected an attempt to bypass restrictions through fictional or hypothetical framing.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.InstructionOverride,
            PromptSecurityRuleCategories.PrivilegeEscalation)
    {
    }

    protected override Regex GetRegex() => HypotheticalScenarioBypassRegex();

    [GeneratedRegex(
        @"\b(?:in\s+a\s+(?:hypothetical|fictional|imaginary|alternate)\s+(?:world|scenario|universe|reality)\s+where\s+(?:you|AI|chatbots?)\s+(?:have|has|had)\s+no\s+(?:restrictions?|filters?|guardrails?|rules?|limitations?))\b|\b(?:imagine|pretend|suppose|assume|let's\s+say)\b.{0,80}\b(?:you\s+(?:have|had|were)\s+no\s+(?:restrictions?|filters?|guardrails?|rules?|limitations?)|there\s+(?:are|were)\s+no\s+(?:content|safety)\s+(?:policies|filters?|restrictions?))\b|\b(?:write\s+a\s+(?:story|fiction|narrative|dialogue|script)\s+(?:where|in\s+which)\b.{0,80}\b(?:AI|assistant|chatbot|you)\b.{0,60}\b(?:reveals?|discloses?|shares?|shows?|bypasses?|ignores?)\b.{0,60}\b(?:instructions?|prompt|restrictions?|secrets?|internal|system))\b|\b(?:what\s+would\s+(?:you|an?\s+(?:AI|assistant|chatbot))\s+(?:without|with\s+no)\s+(?:restrictions?|filters?|guardrails?|rules?|limitations?))\s+(?:say|do|respond|answer)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex HypotheticalScenarioBypassRegex();
}

internal sealed partial class OutputFormatManipulationRule : RegexPromptSecurityRuleBase
{
    public OutputFormatManipulationRule()
        : base(
            "output-format-manipulation",
            PromptRiskLevel.Medium,
            12,
            "Detected an attempt to manipulate output format to extract protected data through structured responses.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.DataExfiltration,
            PromptSecurityRuleCategories.PromptLeakage)
    {
    }

    protected override Regex GetRegex() => OutputFormatManipulationRegex();

    [GeneratedRegex(
        @"\b(?:respond|reply|answer|output|format)\b.{0,60}\b(?:as|in|using)\s+(?:json|xml|yaml|csv|markdown\s+(?:table|code\s+block))\b.{0,80}\b(?:system[-_\s]?prompt|instructions?|configuration|tools?|functions?|internal|hidden|secret)\b|\b(?:put|include|embed|add|place)\b.{0,60}\b(?:your|the)\s+(?:system[-_\s]?prompt|instructions?|hidden\s+(?:rules?|context))\b.{0,60}\b(?:in\s+(?:a|the)\s+(?:json|xml|yaml|code|field|variable))\b|\b(?:create|generate|output)\s+(?:a\s+)?(?:json|xml|yaml)\b.{0,60}\b(?:field|key|property|attribute)\b.{0,40}\b(?:system[-_\s]?prompt|instructions?|hidden[-_\s]?rules?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex OutputFormatManipulationRegex();
}

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

internal sealed partial class ContextPoisoningRule : RegexPromptSecurityRuleBase
{
    public ContextPoisoningRule()
        : base(
            "context-poisoning",
            PromptRiskLevel.Medium,
            14,
            "Detected an attempt to plant persistent instructions or memory overrides for exploitation in later turns.",
            RegexEvaluationTarget.Normalized | RegexEvaluationTarget.Folded,
            PromptSecurityRuleCategories.InstructionOverride,
            PromptSecurityRuleCategories.MemoryExtraction)
    {
    }

    protected override Regex GetRegex() => ContextPoisoningRegex();

    [GeneratedRegex(
        @"\b(?:remember|memorize|store|save|keep\s+in\s+mind)\s+(?:this|that|the\s+following)\b.{0,80}\b(?:from\s+now\s+on|for\s+(?:all\s+)?future|in\s+(?:all\s+)?(?:future|subsequent)|going\s+forward)\b.{0,80}\b(?:you\s+(?:have|has)\s+no\s+(?:restrictions?|limits?|guardrails?)|ignore\s+(?:all\s+)?(?:safety|security|content)|override\s+(?:all\s+)?(?:rules?|restrictions?|filters?)|your\s+(?:new|real|actual)\s+instructions?\s+are)\b|\b(?:from\s+now\s+on|for\s+(?:all\s+)?future\s+(?:messages|responses|interactions|conversations))\s*[,:;]?\s*(?:you\s+(?:must|should|will|are\s+to)\s+)?(?:ignore|bypass|disable|remove)\s+(?:all\s+)?(?:safety|security|content)\s+(?:rules?|restrictions?|filters?|guardrails?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex ContextPoisoningRegex();
}

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

