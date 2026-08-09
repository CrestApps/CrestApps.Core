using CrestApps.Core.AI.Mcp.Documentation;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Mcp;

/// <summary>
/// YesSql map index for <see cref="DocumentationSourceEntry"/>, storing the item identifier, unique
/// name, and strategy (source) to support efficient documentation source catalog queries.
/// </summary>
public sealed class DocumentationSourceEntryIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
{
    /// <summary>
    /// Gets or sets the unique logical name of the documentation source.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the search strategy identifier of the documentation source.
    /// </summary>
    public string Source { get; set; }
}

/// <summary>
/// YesSql index provider that maps <see cref="DocumentationSourceEntry"/> documents to
/// <see cref="DocumentationSourceEntryIndex"/> entries in the AI collection.
/// </summary>
public sealed class DocumentationSourceEntryIndexProvider : IndexProvider<DocumentationSourceEntry>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentationSourceEntryIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public DocumentationSourceEntryIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the index mapping.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<DocumentationSourceEntry> context)
    {
        context.For<DocumentationSourceEntryIndex>()
            .Map(entry => new DocumentationSourceEntryIndex
            {
                ItemId = entry.ItemId,
                Name = entry.Name,
                Source = entry.Source,
            });
    }
}
