using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Indexing;

/// <summary>
/// YesSql index provider that maps <see cref="AIDocument"/> documents
/// to <see cref="AIDocumentIndex"/> entries in the AI docs collection.
/// </summary>
public sealed class AIDocumentIndexProvider : IndexProvider<AIDocument>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIDocumentIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public AIDocumentIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AIDocsCollectionName;
    }

    /// <summary>
    /// Describes the operation.
    /// </summary>
    /// <param name="context">The context.</param>
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
