using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Indexing;

/// <summary>
/// YesSql map index for <see cref="AIDocument"/>, storing the item identifier,
/// external reference, file name, and file extension for efficient document queries.
/// </summary>
public sealed class AIDocumentIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets an external reference identifier associated with this document.
    /// </summary>
    public string ReferenceId { get; set; }

    /// <summary>
    /// Gets or sets the type qualifier for <see cref="ReferenceId"/>,
    /// distinguishing between different reference domains.
    /// </summary>
    public string ReferenceType { get; set; }

    /// <summary>
    /// Gets or sets the original file name of the document, if available.
    /// </summary>
    public string FileName { get; set; }

    /// <summary>
    /// Gets or sets the file extension (including the leading dot) derived from <see cref="FileName"/>,
    /// or <see langword="null"/> when no file name is present.
    /// </summary>
    public string Extension { get; set; }
}

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
