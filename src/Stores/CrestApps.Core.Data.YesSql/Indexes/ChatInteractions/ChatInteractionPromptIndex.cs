using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

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

/// <summary>
/// YesSql index provider that maps <see cref="ChatInteractionPrompt"/> documents
/// to <see cref="ChatInteractionPromptIndex"/> entries in the AI collection.
/// </summary>
public sealed class ChatInteractionPromptIndexProvider : IndexProvider<ChatInteractionPrompt>
{
    public ChatInteractionPromptIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    public override void Describe(DescribeContext<ChatInteractionPrompt> context)
    {
        context.For<ChatInteractionPromptIndex>()
            .Map(prompt => new ChatInteractionPromptIndex
            {
                ItemId = prompt.ItemId,
                ChatInteractionId = prompt.ChatInteractionId,
                Role = prompt.Role.Value,
                CreatedUtc = prompt.CreatedUtc,
            });
    }
}
