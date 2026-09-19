using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Indexing;

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
