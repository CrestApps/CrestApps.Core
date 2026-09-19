using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

/// <summary>
/// YesSql index provider that maps <see cref="AIProviderConnection"/> documents
/// to <see cref="AIProviderConnectionIndex"/> entries in the AI collection.
/// </summary>
public sealed class AIProviderConnectionIndexProvider : IndexProvider<AIProviderConnection>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIProviderConnectionIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public AIProviderConnectionIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the operation.
    /// </summary>
    /// <param name="context">The context.</param>
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
