namespace CrestApps.Core.AI.Security;

/// <summary>
/// Evaluates one prompt security rule against a normalized prompt evaluation context.
/// </summary>
public interface IPromptSecurityRule
{
    /// <summary>
    /// Gets the unique identifier for the rule.
    /// </summary>
    string RuleId { get; }

    /// <summary>
    /// Evaluates the rule against the supplied prompt evaluation context.
    /// </summary>
    /// <param name="context">The normalized evaluation context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A matched rule result when the rule detects suspicious input; otherwise <see langword="null"/>.
    /// </returns>
    ValueTask<PromptSecurityRuleResult> EvaluateAsync(PromptSecurityEvaluationContext context, CancellationToken cancellationToken = default);
}
