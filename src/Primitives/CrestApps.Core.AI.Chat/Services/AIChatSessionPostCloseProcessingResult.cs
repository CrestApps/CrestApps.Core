namespace CrestApps.Core.AI.Chat.Services;

/// <summary>
/// Describes the outcome of a shared post-close processing pass for a chat session.
/// </summary>
public sealed class AIChatSessionPostCloseProcessingResult
{
    /// <summary>
    /// Gets or sets the had Work.
    /// </summary>
    public bool HadWork { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether is Completed.
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// Gets or sets the post Session Tasks Completed Now.
    /// </summary>
    public bool PostSessionTasksCompletedNow { get; set; }

    /// <summary>
    /// Gets or sets the analytics Recorded Now.
    /// </summary>
    public bool AnalyticsRecordedNow { get; set; }

    /// <summary>
    /// Gets or sets the conversion Goals Evaluated Now.
    /// </summary>
    public bool ConversionGoalsEvaluatedNow { get; set; }
}
