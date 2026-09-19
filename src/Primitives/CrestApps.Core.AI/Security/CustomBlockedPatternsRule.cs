using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

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
