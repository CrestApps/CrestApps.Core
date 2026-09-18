using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Knowledge;

/// <summary>
/// YesSql map index for <see cref="KnowledgeObject"/>, keyed by the owning data source (its source) and by
/// the identifiers retrieval and backfill look objects up by.
/// </summary>
public sealed class KnowledgeObjectIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the owning data source identifier (the object's source).
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets the canonical identifier.
    /// </summary>
    public string CanonicalId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the document the object belongs to.
    /// </summary>
    public string RootId { get; set; }

    /// <summary>
    /// Gets or sets what kind of knowledge the object holds.
    /// </summary>
    public string ObjectType { get; set; }

    /// <summary>
    /// Gets or sets how far along the object is.
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// Gets or sets the hash of the bytes, which the description cache looks up by.
    /// </summary>
    public string ContentHash { get; set; }
}

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
