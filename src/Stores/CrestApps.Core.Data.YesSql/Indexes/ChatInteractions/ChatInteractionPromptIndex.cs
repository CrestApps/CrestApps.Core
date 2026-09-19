using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Data.YesSql.Indexes.ChatInteractions;

/// <summary>
/// YesSql map index for <see cref="ChatInteractionPrompt"/>, storing the item identifier,
/// owning interaction identifier, role, and creation timestamp to support efficient prompt queries.
/// </summary>
public sealed class ChatInteractionPromptIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the identifier of the chat interaction that owns this prompt.
    /// </summary>
    public string ChatInteractionId { get; set; }

    /// <summary>
    /// Gets or sets the role of the message author (e.g., <c>user</c>, <c>assistant</c>, <c>system</c>).
    /// </summary>
    public string Role { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when this prompt was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }
}
