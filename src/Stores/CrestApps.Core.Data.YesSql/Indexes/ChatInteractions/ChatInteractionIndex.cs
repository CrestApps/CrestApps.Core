using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Data.YesSql.Indexes.ChatInteractions;

/// <summary>
/// YesSql map index for <see cref="ChatInteraction"/>, storing the item identifier,
/// owner, title, and creation timestamp to support efficient interaction queries.
/// </summary>
public sealed class ChatInteractionIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the identifier of the user who owns this chat interaction.
    /// </summary>
    public string UserId { get; set; }

    /// <summary>
    /// Gets or sets the title of the chat interaction, truncated to 255 characters.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the chat interaction was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }
}
