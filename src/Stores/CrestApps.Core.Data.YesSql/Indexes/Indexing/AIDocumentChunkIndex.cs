using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Indexing;

/// <summary>
/// YesSql map index for <see cref="AIDocumentChunk"/>, storing the item identifier,
/// parent document, external reference, and positional index for efficient chunk queries.
/// </summary>
public sealed class AIDocumentChunkIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the identifier of the parent <see cref="AIDocument"/> that owns this chunk.
    /// </summary>
    public string AIDocumentId { get; set; }

    /// <summary>
    /// Gets or sets an external reference identifier associated with this chunk.
    /// </summary>
    public string ReferenceId { get; set; }

    /// <summary>
    /// Gets or sets the type qualifier for <see cref="ReferenceId"/>,
    /// distinguishing between different reference domains.
    /// </summary>
    public string ReferenceType { get; set; }

    /// <summary>
    /// Gets or sets the zero-based ordinal position of this chunk within its parent document.
    /// </summary>
    public int Index { get; set; }
}

/// <summary>
/// YesSql index provider that maps <see cref="AIDocumentChunk"/> documents
/// to <see cref="AIDocumentChunkIndex"/> entries in the AI docs collection.
/// </summary>
public sealed class AIDocumentChunkIndexProvider : IndexProvider<AIDocumentChunk>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIDocumentChunkIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public AIDocumentChunkIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AIDocsCollectionName;
    }

    /// <summary>
    /// Describes the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<AIDocumentChunk> context)
    {
        context.For<AIDocumentChunkIndex>()
            .Map(chunk => new AIDocumentChunkIndex
            {
                ItemId = chunk.ItemId,
                AIDocumentId = chunk.AIDocumentId,
                ReferenceId = chunk.ReferenceId,
                ReferenceType = chunk.ReferenceType,
                Index = chunk.Index,
            });
    }
}
