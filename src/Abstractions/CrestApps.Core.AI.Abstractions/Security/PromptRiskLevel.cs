namespace CrestApps.Core.AI.Security;

/// <summary>
/// Defines the risk level classification for a prompt security evaluation.
/// </summary>
public enum PromptRiskLevel
{
    /// <summary>
    /// No risk detected; the prompt is safe.
    /// </summary>
    None,

    /// <summary>
    /// Low risk; the prompt contains patterns that are slightly suspicious but likely benign.
    /// </summary>
    Low,

    /// <summary>
    /// Medium risk; the prompt contains patterns that suggest possible manipulation.
    /// </summary>
    Medium,

    /// <summary>
    /// High risk; the prompt contains patterns strongly indicative of prompt injection.
    /// </summary>
    High,

    /// <summary>
    /// Critical risk; the prompt is almost certainly an injection attack.
    /// </summary>
    Critical,
}
