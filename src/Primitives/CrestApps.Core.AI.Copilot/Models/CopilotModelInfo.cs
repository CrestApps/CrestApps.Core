namespace CrestApps.Core.AI.Copilot.Models;

/// <summary>
/// Represents a Copilot model available to the authenticated user.
/// </summary>
public sealed class CopilotModelInfo
{
    /// <summary>
    /// The model identifier (e.g., "gpt-4o", "claude-sonnet-4").
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// The display name of the model.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The premium request cost multiplier (e.g., 1 for standard, 3 for premium).
    /// A value of <c>0</c> means unknown.
    /// </summary>
    public int CostMultiplier { get; set; }
}
