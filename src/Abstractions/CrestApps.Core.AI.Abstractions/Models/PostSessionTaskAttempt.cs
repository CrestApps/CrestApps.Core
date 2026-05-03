namespace CrestApps.Core.AI.Models;

/// <summary>
/// Stores the outcome of one failed or incomplete post-session task attempt.
/// </summary>
public sealed class PostSessionTaskAttempt
{
    /// <summary>
    /// Gets or sets the 1-based attempt number for this task execution.
    /// </summary>
    public int AttemptNumber { get; set; }

    /// <summary>
    /// Gets or sets the task status that remained after this attempt completed.
    /// </summary>
    public PostSessionTaskResultStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the persisted error message for this attempt.
    /// </summary>
    public string ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this attempt outcome was recorded.
    /// </summary>
    public DateTime RecordedAtUtc { get; set; }
}
