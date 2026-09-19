using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.ChatInteractions;

/// <summary>
/// YesSql index provider that maps <see cref="ChatInteraction"/> documents
/// to <see cref="ChatInteractionIndex"/> entries in the AI collection.
/// </summary>
public sealed class ChatInteractionIndexProvider : IndexProvider<ChatInteraction>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChatInteractionIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public ChatInteractionIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<ChatInteraction> context)
    {
        context.For<ChatInteractionIndex>()
            .Map(interaction => new ChatInteractionIndex
            {
                ItemId = interaction.ItemId,
                UserId = interaction.OwnerId,
                Title = interaction.Title?[..Math.Min(interaction.Title.Length, 255)],
                CreatedUtc = interaction.CreatedUtc,
            });
    }
}
