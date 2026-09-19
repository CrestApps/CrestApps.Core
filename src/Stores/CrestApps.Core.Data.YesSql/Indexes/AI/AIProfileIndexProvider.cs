using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

/// <summary>
/// YesSql index provider that maps <see cref="AIProfile"/> documents
/// to <see cref="AIProfileIndex"/> entries in the AI collection.
/// </summary>
public sealed class AIProfileIndexProvider : IndexProvider<AIProfile>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIProfileIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public AIProfileIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<AIProfile> context)
    {
        context.For<AIProfileIndex>()
            .Map(profile => new AIProfileIndex
            {
                ItemId = profile.ItemId,
                Name = profile.Name,
                Source = profile.Source,
                Type = profile.Type.ToString(),
            });
    }
}
