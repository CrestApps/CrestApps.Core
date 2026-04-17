namespace CrestApps.Core.AI.Copilot.Models;

/// <summary>
/// The reasoning effort level for GitHub Copilot sessions.
/// </summary>
public enum CopilotReasoningEffort
{
    /// <summary>
    /// No explicit effort level; use the model default.
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
