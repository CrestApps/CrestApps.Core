using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

/// <summary>
/// YesSql map index for <see cref="AIChatSessionPrompt"/>, storing the item identifier,
/// owning session identifier, and role to support efficient prompt lookups.
/// </summary>
public sealed class AIChatSessionPromptIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the identifier of the chat session that owns this prompt.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the role of the message author (e.g., <c>user</c>, <c>assistant</c>, <c>system</c>).
    /// </summary>
    public string Role { get; set; }
}
