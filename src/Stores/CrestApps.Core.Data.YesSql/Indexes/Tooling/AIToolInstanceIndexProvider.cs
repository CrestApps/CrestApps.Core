using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.Tooling;

/// <summary>
/// YesSql index provider that maps <see cref="AIToolInstance"/> documents
/// to <see cref="AIToolInstanceIndex"/> entries in the AI collection.
/// </summary>
public sealed class AIToolInstanceIndexProvider : IndexProvider<AIToolInstance>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIToolInstanceIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public AIToolInstanceIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the index map for <see cref="AIToolInstance"/> documents.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<AIToolInstance> context)
    {
        context.For<AIToolInstanceIndex>()
            .Map(instance => new AIToolInstanceIndex
            {
                ItemId = instance.ItemId,
                Name = instance.Name,
                Source = instance.Source,
            });
    }
}
