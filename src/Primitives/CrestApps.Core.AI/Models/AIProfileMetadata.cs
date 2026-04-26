namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents the AI Profile Metadata.
/// </summary>
public sealed class AIProfileMetadata
{
    /// <summary>
    /// Gets or sets the system Message.
    /// </summary>
    public string SystemMessage { get; set; }

    /// <summary>
    /// Gets or sets the initial Prompt.
    /// </summary>
    public string InitialPrompt { get; set; }

    /// <summary>
    /// Gets or sets the temperature.
    /// </summary>
    public float? Temperature { get; set; }

    /// <summary>
    /// Gets or sets the top P.
    /// </summary>
    public float? TopP { get; set; }

    /// <summary>
    /// Gets or sets the frequency Penalty.
    /// </summary>
    public float? FrequencyPenalty { get; set; }

    /// <summary>
    /// Gets or sets the presence Penalty.
    /// </summary>
    public float? PresencePenalty { get; set; }

    /// <summary>
    /// Gets or sets the max Tokens.
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Gets or sets the past Messages Count.
    /// </summary>
    public int? PastMessagesCount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether uses Caching.
    /// </summary>
    public bool UseCaching { get; set; } = true;
}
