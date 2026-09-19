using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.FileSources;

/// <summary>
/// YesSql index provider that maps <see cref="IngestionItemState"/> documents to
/// <see cref="IngestionItemStateIndex"/> entries in the AI collection.
/// </summary>
public sealed class IngestionItemStateIndexProvider : IndexProvider<IngestionItemState>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionItemStateIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public IngestionItemStateIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the index mapping.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<IngestionItemState> context)
    {
        context.For<IngestionItemStateIndex>()
            .Map(state => new IngestionItemStateIndex
            {
                ItemId = state.ItemId,
                Source = state.Source,
                ItemKey = state.ItemKey,
            });
    }
}
