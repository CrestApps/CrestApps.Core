using CrestApps.Core.Data.YesSql.Indexes;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging;
using YesSql;
using YesSql.Services;

namespace CrestApps.Core.Data.YesSql.Services;

public class DocumentCatalog<T, TIndex> : ICatalog<T>
    where T : CatalogItem
    where TIndex : CatalogItemIndex
{
    private const int MaxGetAllResults = 10000;

    protected string CollectionName { get; }

    protected readonly ISession Session;
    protected readonly ILogger Logger;

    public DocumentCatalog(
        ISession session,
        string collectionName = null,
        ILogger<DocumentCatalog<T, TIndex>> logger = null)
    {
        Session = session;
        Logger = logger;
        CollectionName = collectionName;
    }

    public async ValueTask<bool> DeleteAsync(T entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await DeletingAsync(entry);

        Session.Delete(entry, CollectionName);

        return true;
    }

    public async ValueTask<T> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        var item = await Session.Query<T, TIndex>(x => x.ItemId == id, collection: CollectionName).FirstOrDefaultAsync(cancellationToken);

        return item;
    }

    public async ValueTask<IReadOnlyCollection<T>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var items = await Session.Query<T, TIndex>(x => x.ItemId.IsIn(ids), collection: CollectionName).ListAsync(cancellationToken);

        return items.ToArray();
    }

    public async ValueTask<PageResult<T>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
        where TQuery : QueryContext
    {
        IQuery<T> query = Session.Query<T, TIndex>(collection: CollectionName);

        if (context is not null)
        {
            if (!string.IsNullOrEmpty(context.Name))
            {
                if (typeof(INameAwareIndex).IsAssignableFrom(typeof(TIndex)))
                {
                    if (context.Sorted)
                    {
                        query = query.With<INameAwareIndex>(x => x.Name.Contains(context.Name))
                            .OrderBy(x => x.Name);
                    }
                    else
                    {
                        query = query.With<INameAwareIndex>(x => x.Name.Contains(context.Name));
                    }
                }
                else if (typeof(IDisplayTextAwareIndex).IsAssignableFrom(typeof(TIndex)))
                {
                    if (context.Sorted)
                    {
                        query = query.With<IDisplayTextAwareIndex>(x => x.DisplayText.Contains(context.Name))
                            .OrderBy(x => x.DisplayText);
                    }
                    else
                    {
                        query = query.With<IDisplayTextAwareIndex>(x => x.DisplayText.Contains(context.Name));
                    }
                }
            }

            if (!string.IsNullOrEmpty(context.Source) && typeof(ISourceAwareIndex).IsAssignableFrom(typeof(TIndex)))
            {
                query = query.With<ISourceAwareIndex>(x => x.Source == context.Source);
            }

            await PagingAsync(query, context);
        }

        var skip = (page - 1) * pageSize;

        return new PageResult<T>
        {
            Count = await query.CountAsync(cancellationToken),
            Entries = (await query.Skip(skip).Take(pageSize).ListAsync(cancellationToken)).ToArray()
        };
    }

    protected virtual ValueTask PagingAsync<TQuery>(IQuery<T> query, TQuery context)
        where TQuery : QueryContext
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask<IReadOnlyCollection<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var items = (await Session.Query<T, TIndex>(collection: CollectionName).Take(MaxGetAllResults + 1).ListAsync(cancellationToken)).ToList();

        if (items.Count > MaxGetAllResults)
        {
            Logger?.LogWarning(
                "GetAllAsync for {EntityType} returned more than {MaxResults} results. The result set has been truncated.",
                typeof(T).Name, MaxGetAllResults);

            items.RemoveAt(items.Count - 1);
        }

        return items.ToArray();
    }

    public async ValueTask CreateAsync(T record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrEmpty(record.ItemId))
        {
            record.ItemId = UniqueId.GenerateId();
        }

        await SavingAsync(record);

        await Session.SaveAsync(record, CollectionName);
    }

    public async ValueTask UpdateAsync(T record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrEmpty(record.ItemId))
        {
            record.ItemId = UniqueId.GenerateId();
        }

        await SavingAsync(record);

        await Session.SaveAsync(record, CollectionName);
    }

    protected virtual ValueTask DeletingAsync(T model)
    {
        return ValueTask.CompletedTask;
    }

    protected virtual ValueTask SavingAsync(T record)
    {
        return ValueTask.CompletedTask;
    }
}
