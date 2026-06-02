namespace CrestApps.Core.AI.Security;

/// <summary>
/// Represents the final disposition of a prompt security evaluation.
/// </summary>
public enum PromptSecurityDisposition
{
    /// <summary>
    /// No actionable indicators were detected.
    /// </summary>
    Safe,

    /// <summary>
    /// Suspicious indicators were detected but the prompt did not cross the blocking threshold.
    /// </summary>
    Flagged,

    /// <summary>
    /// The prompt crossed the configured blocking threshold and must be rejected.
    /// </summary>
    Blocked,
}
