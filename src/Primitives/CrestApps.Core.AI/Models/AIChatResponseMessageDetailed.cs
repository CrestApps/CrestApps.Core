namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents the AI Chat Response Message Detailed.
/// </summary>
public sealed class AIChatResponseMessageDetailed : AIResponseMessage
{
    /// <summary>
    /// Gets or sets the ID.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the role.
    /// </summary>
    public string Role { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether is Generated Prompt.
    /// </summary>
    public bool IsGeneratedPrompt { get; set; }

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether uses r Rating.
    /// </summary>
    public bool? UserRating { get; set; }

    /// <summary>
    /// Gets or sets the references.
    /// </summary>
    public Dictionary<string, AICompletionReference> References { get; set; }
}
