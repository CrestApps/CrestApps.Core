using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

/// <summary>
/// YesSql map index for <see cref="AIProfileTemplate"/>, storing the item identifier,
/// unique name, and source to support efficient catalog queries.
/// </summary>
public sealed class AIProfileTemplateIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
{
    /// <summary>
    /// Gets or sets the unique technical name of the AI profile template.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the source or provider name of the AI profile template.
    /// </summary>
    public string Source { get; set; }
}

/// <summary>
/// YesSql index provider that maps <see cref="AIProfileTemplate"/> documents
/// to <see cref="AIProfileTemplateIndex"/> entries in the AI collection.
/// </summary>
public sealed class AIProfileTemplateIndexProvider : IndexProvider<AIProfileTemplate>
{
    public AIProfileTemplateIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    public override void Describe(DescribeContext<AIProfileTemplate> context)
    {
        context.For<AIProfileTemplateIndex>()
            .Map(template => new AIProfileTemplateIndex
            {
                ItemId = template.ItemId,
                Name = template.Name,
                Source = template.Source,
            });
    }
}
