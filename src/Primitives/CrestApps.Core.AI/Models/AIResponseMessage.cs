namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents the AI Response Message.
/// </summary>
public class AIResponseMessage
{
    /// <summary>
    /// Gets or sets the content.
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// Gets or sets the appearance.
    /// </summary>
    public AssistantMessageAppearance Appearance { get; set; }
}
