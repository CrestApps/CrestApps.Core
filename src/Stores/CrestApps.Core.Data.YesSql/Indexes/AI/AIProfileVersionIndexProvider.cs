using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

/// <summary>
/// Maps <see cref="AIProfileVersion"/> documents to <see cref="AIProfileVersionIndex"/> rows in the AI collection.
/// </summary>
public sealed class AIProfileVersionIndexProvider : IndexProvider<AIProfileVersion>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIProfileVersionIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The YesSql store options that name the AI collection.</param>
    public AIProfileVersionIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the index rows of a version.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<AIProfileVersion> context)
    {
        context.For<AIProfileVersionIndex>()
            .Map(version => new AIProfileVersionIndex
            {
                ItemId = version.ItemId,
                ProfileId = version.ProfileId,
                Version = version.Version,
                CreatedUtc = version.CreatedUtc,
            });
    }
}
