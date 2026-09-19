using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.DataSources;

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
        CollectionName = options.Value.AICollectionName;
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
                Source = ds.Source,
                SourceIndexProfileName = ds.SourceIndexProfileName,
            });
    }
}
