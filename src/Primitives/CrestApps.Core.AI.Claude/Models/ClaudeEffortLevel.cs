namespace CrestApps.Core.AI.Claude.Models;

/// <summary>
/// The reasoning effort level for Anthropic extended thinking.
/// Maps to the <c>budget_tokens</c> parameter.
/// </summary>
public enum ClaudeEffortLevel
{
    /// <summary>
    /// No effort level specified; the API default is used.
    /// </summary>
    None = 0,

    /// <summary>
    /// Low reasoning effort.
    /// </summary>
    Low = 1,

    /// <summary>
    /// Medium reasoning effort.
    /// </summary>
    Medium = 2,

    /// <summary>
    /// High reasoning effort.
    /// </summary>
    High = 3,
}
