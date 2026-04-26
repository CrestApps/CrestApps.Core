using CrestApps.Core.Data.YesSql.Indexes;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

public class NamedDocumentCatalog<T, TIndex> : DocumentCatalog<T, TIndex>, INamedCatalog<T>
    where T : CatalogItem, INameAwareModel
    where TIndex : CatalogItemIndex, INameAwareIndex
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NamedDocumentCatalog"/> class.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="logger">The logger.</param>
    public NamedDocumentCatalog(
        ISession session,
        string collectionName = null,
        ILogger<DocumentCatalog<T, TIndex>> logger = null)
        : base(session, collectionName, logger)
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

        var item = await Session.Query<T, TIndex>(x => x.Name == name, collection: CollectionName).FirstOrDefaultAsync(cancellationToken);

return item;
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
