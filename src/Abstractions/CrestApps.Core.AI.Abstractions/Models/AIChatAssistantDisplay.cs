namespace CrestApps.Core.AI.Models;

/// <summary>
/// How a chat surface presents the assistant: the name and avatar shown on its messages. A surface passes these
/// values to the chat UI configuration; each one left <see langword="null"/> keeps the surface's default look.
/// </summary>
public sealed class AIChatAssistantDisplay
{
    /// <summary>
    /// Gets a display that changes nothing, used when the profile or interaction links to no persona.
    /// </summary>
    public static AIChatAssistantDisplay Default { get; } = new();

    /// <summary>
    /// Gets the name shown on assistant messages, or <see langword="null"/> to keep the surface's default label.
    /// </summary>
    public string AssistantName { get; init; }

    /// <summary>
    /// Gets the avatar image URL shown on assistant messages, or <see langword="null"/> to keep the default icon.
    /// </summary>
    public string AvatarUrl { get; init; }

    /// <summary>
    /// Returns the name to show on assistant messages.
    /// </summary>
    /// <param name="defaultLabel">The surface's own label, used when there is no assistant name.</param>
    public string GetAssistantLabel(string defaultLabel)
        => string.IsNullOrWhiteSpace(AssistantName)
            ? defaultLabel
            : AssistantName;
}
