using CrestApps.Core.AI.Models;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AIMemory;

public sealed class AIMemoryEntryIndex : CatalogItemIndex
{
    public string UserId { get; set; }

    public string Name { get; set; }
}

public sealed class AIMemoryEntryIndexProvider : IndexProvider<AIMemoryEntry>
{
    internal AIMemoryEntryIndexProvider(string collectionName = null)
    {
        CollectionName = collectionName;
    }

    public override void Describe(DescribeContext<AIMemoryEntry> context)
    {
        context.For<AIMemoryEntryIndex>()
            .Map(entry => new AIMemoryEntryIndex
            {
                ItemId = entry.ItemId,
                UserId = entry.UserId,
                Name = entry.Name,
            });
    }
}
