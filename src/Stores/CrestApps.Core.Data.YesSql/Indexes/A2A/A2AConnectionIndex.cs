using CrestApps.Core.AI.A2A.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.A2A;

/// <summary>
/// YesSql map index for <see cref="A2AConnection"/>, storing the item identifier
/// and display text to support efficient catalog queries.
/// </summary>
public sealed class A2AConnectionIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the human-readable display text of the A2A connection.
    /// </summary>
    public string DisplayText { get; set; }
}

/// <summary>
/// YesSql index provider that maps <see cref="A2AConnection"/> documents
/// to <see cref="A2AConnectionIndex"/> entries in the AI collection.
/// </summary>
public sealed class A2AConnectionIndexProvider : IndexProvider<A2AConnection>
{
    public A2AConnectionIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    public override void Describe(DescribeContext<A2AConnection> context)
    {
        context.For<A2AConnectionIndex>()
            .Map(connection => new A2AConnectionIndex
            {
                ItemId = connection.ItemId,
                DisplayText = connection.DisplayText,
            });
    }
}
