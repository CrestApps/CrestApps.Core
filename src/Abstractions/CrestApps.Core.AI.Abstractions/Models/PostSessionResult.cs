using System.Text.Json.Serialization;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Stores the result of a single post-session processing task.
/// </summary>
public sealed class PostSessionResult
{
    /// <summary>
    /// Gets or sets the name of the task that produced this result.
    /// Matches the <see cref="PostSessionTask.Name"/> from the profile configuration.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the AI-generated result value.
    /// For Disposition: the selected option. For Summary: the generated text. For Sentiment: Positive/Negative/Neutral.
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// Gets or sets the processing status of this individual task.
    /// </summary>
    public PostSessionTaskResultStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the current in-memory error message if the task failed.
    /// Persisted troubleshooting details live in <see cref="AttemptHistory"/>.
    /// </summary>
    [JsonIgnore]
    public string ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the number of processing attempts made for this task.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Gets or sets the history of failed or incomplete attempts for this task.
    /// </summary>
    public List<PostSessionTaskAttempt> AttemptHistory { get; set; } = [];

    /// <summary>
    /// Gets or sets the UTC timestamp when this task reached a terminal processed state.
    /// This is only populated when the task succeeds or reaches a final failure state.
    /// </summary>
    public DateTime? ProcessedAtUtc { get; set; }
}
