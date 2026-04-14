using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Indexing;

public sealed class AIDocumentIndex : CatalogItemIndex
{
    public string ReferenceId { get; set; }

    public string ReferenceType { get; set; }

    public string FileName { get; set; }

    public string Extension { get; set; }
}

public sealed class AIDocumentIndexProvider : IndexProvider<AIDocument>
{
    public AIDocumentIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AIDocsCollectionName;
    }

    public override void Describe(DescribeContext<AIDocument> context)
    {
        context.For<AIDocumentIndex>()
            .Map(doc =>
            {
                var index = new AIDocumentIndex
                {
                    ItemId = doc.ItemId,
                    ReferenceId = doc.ReferenceId,
                    ReferenceType = doc.ReferenceType,
                };

                if (!string.IsNullOrEmpty(doc.FileName))
                {
                    index.FileName = doc.FileName;
                    index.Extension = Path.GetExtension(doc.FileName);
                }

                return index;
            });
    }
}
