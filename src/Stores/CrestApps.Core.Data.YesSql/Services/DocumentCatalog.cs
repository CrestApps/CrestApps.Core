using System.Linq.Expressions;
using System.Reflection;
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

    private static readonly MethodInfo _stringContainsMethod = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)]);
    private static readonly PropertyInfo _nameProperty = typeof(INameAwareIndex).GetProperty(nameof(INameAwareIndex.Name));
    private static readonly PropertyInfo _displayTextProperty = typeof(IDisplayTextAwareIndex).GetProperty(nameof(IDisplayTextAwareIndex.DisplayText));
    private static readonly PropertyInfo _sourceProperty = typeof(ISourceAwareIndex).GetProperty(nameof(ISourceAwareIndex.Source));

    protected string CollectionName { get; }

    protected readonly ISession Session;
    protected readonly ILogger Logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentCatalog"/> class.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="logger">The logger.</param>
    public DocumentCatalog(
        ISession session,
        string collectionName = null,
        ILogger<DocumentCatalog<T, TIndex>> logger = null)
    {
        Session = session;
        Logger = logger;
        CollectionName = collectionName;
    }

    /// <summary>
    /// Deletes the operation.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<bool> DeleteAsync(T entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await DeletingAsync(entry);

        Session.Delete(entry, CollectionName);

        return true;
    }

    /// <summary>
    /// Finds by id.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<T> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        var item = await Session.Query<T, TIndex>(x => x.ItemId == id, collection: CollectionName).FirstOrDefaultAsync(cancellationToken);

        return item;
    }

    /// <summary>
    /// Gets the operation.
    /// </summary>
    /// <param name="ids">The ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IReadOnlyCollection<T>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var items = await Session.Query<T, TIndex>(x => x.ItemId.IsIn(ids), collection: CollectionName).ListAsync(cancellationToken);

        return items.ToArray();
    }

    /// <summary>
    /// Pages the operation.
    /// </summary>
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
                    query = FilterByText(query, _nameProperty, context.Name, context.Sorted);
                }
                else if (typeof(IDisplayTextAwareIndex).IsAssignableFrom(typeof(TIndex)))
                {
                    query = FilterByText(query, _displayTextProperty, context.Name, context.Sorted);
                }
            }

            if (!string.IsNullOrEmpty(context.Source) && typeof(ISourceAwareIndex).IsAssignableFrom(typeof(TIndex)))
            {
                var parameter = Expression.Parameter(typeof(TIndex), "x");
                var predicate = Expression.Lambda<Func<TIndex, bool>>(
                    Expression.Equal(Expression.Property(parameter, _sourceProperty), Expression.Constant(context.Source)),
                    parameter);

                query = query.With(predicate);
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

    /// <summary>
    /// Filters the query to the entries whose <paramref name="property"/> contains <paramref name="value"/>.
    /// The predicate is built for <typeparamref name="TIndex"/>, because YesSql maps an index type to its table
    /// and a marker interface such as <see cref="IDisplayTextAwareIndex"/> has no table.
    /// </summary>
    private static IQuery<T> FilterByText(IQuery<T> query, PropertyInfo property, string value, bool sorted)
    {
        var parameter = Expression.Parameter(typeof(TIndex), "x");
        var member = Expression.Property(parameter, property);
        var predicate = Expression.Lambda<Func<TIndex, bool>>(
            Expression.Call(member, _stringContainsMethod, Expression.Constant(value)),
            parameter);

        var filtered = query.With(predicate);

        if (sorted)
        {
            return filtered.OrderBy(Expression.Lambda<Func<TIndex, object>>(member, parameter));
        }

        return filtered;
    }

    /// <summary>
    /// Pagings the operation.
    /// </summary>
    protected virtual ValueTask PagingAsync<TQuery>(IQuery<T> query, TQuery context)
        where TQuery : QueryContext
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Gets all.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
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

    /// <summary>
    /// Creates the operation.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask CreateAsync(T record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrEmpty(record.ItemId))
        {
            record.ItemId = UniqueId.GenerateId();
        }

        await SavingAsync(record);

        await Session.SaveAsync(record, false, CollectionName, CancellationToken.None);
    }

    /// <summary>
    /// Updates the operation.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask UpdateAsync(T record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrEmpty(record.ItemId))
        {
            record.ItemId = UniqueId.GenerateId();
        }

        await SavingAsync(record);

        await Session.SaveAsync(record, false, CollectionName, CancellationToken.None);
    }

    /// <summary>
    /// Deletings the operation.
    /// </summary>
    /// <param name="model">The model.</param>
    protected virtual ValueTask DeletingAsync(T model)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Savings the operation.
    /// </summary>
    /// <param name="record">The record.</param>
    protected virtual ValueTask SavingAsync(T record)
    {
        return ValueTask.CompletedTask;
    }
}
