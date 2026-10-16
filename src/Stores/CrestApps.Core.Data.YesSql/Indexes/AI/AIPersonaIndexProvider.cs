using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

/// <summary>
/// YesSql index provider that maps <see cref="AIPersona"/> documents to <see cref="AIPersonaIndex"/> entries in the
/// AI collection.
/// </summary>
public sealed class AIPersonaIndexProvider : IndexProvider<AIPersona>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIPersonaIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The YesSql store options.</param>
    public AIPersonaIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the index mapping.
    /// </summary>
    /// <param name="context">The describe context.</param>
    public override void Describe(DescribeContext<AIPersona> context)
    {
        context.For<AIPersonaIndex>()
            .Map(persona => new AIPersonaIndex
            {
                ItemId = persona.ItemId,
                Name = persona.Name,
            });
    }
}
