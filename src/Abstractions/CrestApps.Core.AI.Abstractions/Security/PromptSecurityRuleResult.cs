namespace CrestApps.Core.AI.Security;

/// <summary>
/// Represents one matched prompt security rule and its scoring metadata.
/// </summary>
public sealed class PromptSecurityRuleResult
{
    /// <summary>
    /// Gets or sets the unique identifier of the matched rule.
    /// </summary>
    public string RuleId { get; set; }

    /// <summary>
    /// Gets or sets the broad category tags associated with the rule.
    /// </summary>
    public IReadOnlyList<string> Categories { get; set; } = [];

    /// <summary>
    /// Gets or sets the severity assigned to the matched rule.
    /// </summary>
    public PromptRiskLevel Severity { get; set; }

    /// <summary>
    /// Gets or sets the numeric score contribution assigned to the matched rule.
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// Gets or sets the operator-facing reason for the match.
    /// </summary>
    public string Reason { get; set; }

    /// <summary>
    /// Gets or sets an optional metadata map describing how the rule matched.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets a value indicating whether the rule matched only after homoglyph folding.
    /// </summary>
    public bool MatchedOnFoldedInput { get; set; }
}
