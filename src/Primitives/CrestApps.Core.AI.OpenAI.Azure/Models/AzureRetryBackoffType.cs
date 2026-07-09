namespace CrestApps.Core.AI.OpenAI.Azure.Models;

/// <summary>
/// Represents the supported Azure OpenAI SDK retry backoff strategies.
/// </summary>
public enum AzureRetryBackoffType
{
    /// <summary>
    /// Uses the same base delay for each retry attempt.
    /// </summary>
    Constant,

    /// <summary>
    /// Doubles the base delay for each retry attempt.
    /// </summary>
    Exponential,
}
