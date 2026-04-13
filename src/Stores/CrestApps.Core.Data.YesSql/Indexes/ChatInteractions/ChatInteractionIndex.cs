using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.ChatInteractions;

public sealed class ChatInteractionIndex : CatalogItemIndex
{
    public string UserId { get; set; }

    public string Title { get; set; }

    public DateTime CreatedUtc { get; set; }
}

public sealed class ChatInteractionIndexProvider : IndexProvider<ChatInteraction>
{
    public ChatInteractionIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

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
