using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

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
