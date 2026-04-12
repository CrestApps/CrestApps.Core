using CrestApps.Core.AI.Models;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Indexing;

public sealed class AIDocumentIndex : CatalogItemIndex
{
    public string ReferenceId { get; set; }

    public string ReferenceType { get; set; }

    public string FileName { get; set; }
}

public sealed class AIDocumentIndexProvider : IndexProvider<AIDocument>
{
    internal AIDocumentIndexProvider(string collectionName = null)
    {
        CollectionName = collectionName;
    }

    public override void Describe(DescribeContext<AIDocument> context)
    {
        context.For<AIDocumentIndex>()
            .Map(doc => new AIDocumentIndex
            {
                ItemId = doc.ItemId,
                ReferenceId = doc.ReferenceId,
                ReferenceType = doc.ReferenceType,
                FileName = doc.FileName,
            });
    }
}
