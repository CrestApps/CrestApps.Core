using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Indexing;

public sealed class AIDocumentChunkIndex : CatalogItemIndex
{
    public string AIDocumentId { get; set; }

    public string ReferenceId { get; set; }

    public string ReferenceType { get; set; }

    public int Index { get; set; }
}

public sealed class AIDocumentChunkIndexProvider : IndexProvider<AIDocumentChunk>
{
    public AIDocumentChunkIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AIDocsCollectionName;
    }

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
