using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

/// <summary>
/// YesSql map index for <see cref="AIProfile"/>, storing the item identifier,
/// unique name, and source to support efficient catalog queries.
/// </summary>
public sealed class AIProfileIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
{
    /// <summary>
    /// Gets or sets the unique technical name of the AI profile.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the source or provider name of the AI profile.
    /// </summary>
    public string Source { get; set; }
}

/// <summary>
/// YesSql index provider that maps <see cref="AIProfile"/> documents
/// to <see cref="AIProfileIndex"/> entries in the AI collection.
/// </summary>
public sealed class AIProfileIndexProvider : IndexProvider<AIProfile>
{
    public AIProfileIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    public override void Describe(DescribeContext<AIProfile> context)
    {
        context.For<AIProfileIndex>()
            .Map(profile => new AIProfileIndex
            {
                ItemId = profile.ItemId,
                Name = profile.Name,
                Source = profile.Source,
            });
    }
}
