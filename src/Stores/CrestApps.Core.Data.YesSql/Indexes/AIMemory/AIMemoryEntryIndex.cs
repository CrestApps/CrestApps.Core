using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AIMemory;

/// <summary>
/// YesSql map index for <see cref="AIMemoryEntry"/>, storing the item identifier,
/// owning user, and entry name to support efficient memory lookups.
/// </summary>
public sealed class AIMemoryEntryIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the identifier of the user who owns this memory entry.
    /// </summary>
    public string UserId { get; set; }

    /// <summary>
    /// Gets or sets the unique technical name of the memory entry.
    /// </summary>
    public string Name { get; set; }
}

/// <summary>
/// YesSql index provider that maps <see cref="AIMemoryEntry"/> documents
/// to <see cref="AIMemoryEntryIndex"/> entries in the AI memory collection.
/// </summary>
public sealed class AIMemoryEntryIndexProvider : IndexProvider<AIMemoryEntry>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIMemoryEntryIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public AIMemoryEntryIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AIMemoryCollectionName;
    }

    /// <summary>
    /// Describes the operation.
    /// </summary>
    /// <param name="context">The context.</param>
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
