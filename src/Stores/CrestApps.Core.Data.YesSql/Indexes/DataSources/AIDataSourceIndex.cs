using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.DataSources;

/// <summary>
/// YesSql map index for <see cref="AIDataSource"/>, storing the item identifier,
/// display text, and associated search index profile name for efficient data-source queries.
/// </summary>
public sealed class AIDataSourceIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the human-readable display text of the AI data source.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the name of the search index profile that backs this data source.
    /// </summary>
    public string SourceIndexProfileName { get; set; }
}

/// <summary>
/// YesSql index provider that maps <see cref="AIDataSource"/> documents
/// to <see cref="AIDataSourceIndex"/> entries in the AI docs collection.
/// </summary>
public sealed class AIDataSourceIndexProvider : IndexProvider<AIDataSource>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIDataSourceIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public AIDataSourceIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AIDocsCollectionName;
    }

    /// <summary>
    /// Describes the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<AIDataSource> context)
    {
        context.For<AIDataSourceIndex>()
            .Map(ds => new AIDataSourceIndex
            {
                ItemId = ds.ItemId,
                DisplayText = ds.DisplayText,
                SourceIndexProfileName = ds.SourceIndexProfileName,
            });
    }
}
