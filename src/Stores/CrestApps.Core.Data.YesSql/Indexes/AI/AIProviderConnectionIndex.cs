using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

/// <summary>
/// YesSql map index for <see cref="AIProviderConnection"/>, storing the item identifier,
/// unique name, and source to support efficient catalog queries.
/// </summary>
public sealed class AIProviderConnectionIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
{
    /// <summary>
    /// Gets or sets the unique technical name of the AI provider connection.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the source or provider name of the AI provider connection.
    /// </summary>
    public string Source { get; set; }
}

/// <summary>
/// YesSql index provider that maps <see cref="AIProviderConnection"/> documents
/// to <see cref="AIProviderConnectionIndex"/> entries in the AI collection.
/// </summary>
public sealed class AIProviderConnectionIndexProvider : IndexProvider<AIProviderConnection>
{
    public AIProviderConnectionIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    public override void Describe(DescribeContext<AIProviderConnection> context)
    {
        context.For<AIProviderConnectionIndex>()
            .Map(connection => new AIProviderConnectionIndex
            {
                ItemId = connection.ItemId,
                Name = connection.Name,
                Source = connection.Source,
            });
    }
}
