using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Knowledge;

/// <summary>
/// YesSql index provider that maps <see cref="KnowledgeObject"/> documents to
/// <see cref="KnowledgeObjectIndex"/> entries in the AI collection.
/// </summary>
public sealed class KnowledgeObjectIndexProvider : IndexProvider<KnowledgeObject>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KnowledgeObjectIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public KnowledgeObjectIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the index mapping.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<KnowledgeObject> context)
    {
        context.For<KnowledgeObjectIndex>()
            .Map(entry => new KnowledgeObjectIndex
            {
                ItemId = entry.ItemId,
                Source = entry.Source,
                CanonicalId = entry.CanonicalId,
                RootId = entry.RootId,
                ObjectType = entry.ObjectType,
                Status = entry.Status,
                ContentHash = entry.ContentHash,
            });
    }
}
