using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

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
