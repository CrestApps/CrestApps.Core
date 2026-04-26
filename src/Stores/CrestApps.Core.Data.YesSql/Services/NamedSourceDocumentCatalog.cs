using CrestApps.Core.Data.YesSql.Indexes;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

public class NamedSourceDocumentCatalog<T, TIndex> : SourceDocumentCatalog<T, TIndex>, INamedSourceCatalog<T>, ISourceCatalog<T>
    where T : CatalogItem, INameAwareModel, ISourceAwareModel
    where TIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NamedSourceDocumentCatalog"/> class.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="collectionName">The collection name.</param>
    public NamedSourceDocumentCatalog(
        ISession session,
        string collectionName = null)
        : base(session, collectionName)
    {
    }

    /// <summary>
    /// Finds by name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<T> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return await Session.Query<T, TIndex>(x => x.Name == name, collection: CollectionName).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the operation.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="source">The source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<T> GetAsync(string name, string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(source);

        return await Session.Query<T, TIndex>(x => x.Name == name && x.Source == source, collection: CollectionName).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Savings the operation.
    /// </summary>
    /// <param name="record">The record.</param>
    protected override async ValueTask SavingAsync(T record)
    {
        var item = await Session.QueryIndex<TIndex>(x => x.Name == record.Name && x.ItemId != record.ItemId, collection: CollectionName).FirstOrDefaultAsync();

        if (item is not null)
        {
            throw new InvalidOperationException("There is already another model with the same name.");
        }
    }
}
