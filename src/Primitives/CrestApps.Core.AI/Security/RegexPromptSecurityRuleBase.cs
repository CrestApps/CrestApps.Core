using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Services;

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
