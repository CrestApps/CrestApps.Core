using CrestApps.Core.AI.Models;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

public sealed class AIChatSessionPromptIndex : CatalogItemIndex
{
    public string SessionId { get; set; }

    public string Role { get; set; }
}

public sealed class AIChatSessionPromptIndexProvider : IndexProvider<AIChatSessionPrompt>
{
    internal AIChatSessionPromptIndexProvider(string collectionName = null)
    {
        CollectionName = collectionName;
    }

    public override void Describe(DescribeContext<AIChatSessionPrompt> context)
    {
        context.For<AIChatSessionPromptIndex>()
            .Map(prompt => new AIChatSessionPromptIndex
            {
                ItemId = prompt.ItemId,
                SessionId = prompt.SessionId,
                Role = prompt.Role.Value,
            });
    }
}
