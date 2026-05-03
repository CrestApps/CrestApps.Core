namespace CrestApps.Core.AI.Models;

/// <summary>
/// Global site settings for shared AI chat session lifecycle processing.
/// </summary>
public sealed class AIChatSessionProcessingOptions
{
    /// <summary>
    /// The default maximum number of post-close retry attempts for a session.
    /// </summary>
    public const int DefaultMaxPostCloseAttempts = 5;

    /// <summary>
    /// Gets or sets the maximum number of post-close retry attempts before processing is treated as terminally failed.
    /// </summary>
    public int MaxPostCloseAttempts { get; set; } = DefaultMaxPostCloseAttempts;
}
