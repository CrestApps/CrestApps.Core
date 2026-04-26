using CrestApps.Core.Data.EntityCore.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

public class DocumentCatalog<T> : ICatalog<T> where T : CatalogItem
{
    private const int MaxGetAllResults = 10000;

    protected readonly CrestAppsEntityDbContext DbContext;
    protected readonly ILogger Logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentCatalog"/> class.
    /// </summary>
    /// <param name="dbContext">The db context.</param>
    /// <param name="logger">The logger.</param>
    public DocumentCatalog(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<T>> logger = null)
    {
        DbContext = dbContext;
        Logger = logger;
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
        var existing = await GetTrackedQuery().FirstOrDefaultAsync(x => x.ItemId == entry.ItemId, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        DbContext.CatalogRecords.Remove(existing);

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

        var record = await GetReadQuery().FirstOrDefaultAsync(x => x.ItemId == id, cancellationToken);

        return record is null ? null : CatalogRecordFactory.Materialize<T>(record);
    }

    /// <summary>
    /// Gets the operation.
    /// </summary>
    /// <param name="ids">The ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IReadOnlyCollection<T>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var itemIds = ids.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        if (itemIds.Length == 0)
        {
            return [];
        }

        var records = await GetReadQuery().Where(x => itemIds.Contains(x.ItemId)).ToListAsync(cancellationToken);

        return records.Select(CatalogRecordFactory.Materialize<T>).ToArray();
    }

    /// <summary>
    /// Pages the operation.
    /// </summary>
    public async ValueTask<PageResult<T>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
        where TQuery : QueryContext
    {
        var query = GetReadQuery();
        var ordered = false;
        if (context is not null)
        {
            if (!string.IsNullOrEmpty(context.Name))
            {
                if (typeof(INameAwareModel).IsAssignableFrom(typeof(T)))
                {
                    query = query.Where(x => x.Name != null && x.Name.Contains(context.Name));
                    if (context.Sorted)
                    {
                        query = query.OrderBy(x => x.Name);
                        ordered = true;
                    }
                }
                else if (typeof(IDisplayTextAwareModel).IsAssignableFrom(typeof(T)))
                {
                    query = query.Where(x => x.DisplayText != null && x.DisplayText.Contains(context.Name));
                    if (context.Sorted)
                    {
                        query = query.OrderBy(x => x.DisplayText);
                        ordered = true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(context.Source) && typeof(ISourceAwareModel).IsAssignableFrom(typeof(T)))
            {
                query = query.Where(x => x.Source == context.Source);
            }

            query = ApplyPaging(query, context);
        }

        if (!ordered)
        {
            query = query.OrderBy(x => x.ItemId);
        }

        var skip = (page - 1) * pageSize;
        var count = await query.CountAsync(cancellationToken);
        var records = await query.Skip(skip).Take(pageSize).ToListAsync(cancellationToken);

        return new PageResult<T>
        {
            Count = count,
            Entries = records.Select(CatalogRecordFactory.Materialize<T>).ToArray(),
        };
    }

    /// <summary>
    /// Applies paging.
    /// </summary>
    protected virtual IQueryable<CatalogRecord> ApplyPaging<TQuery>(IQueryable<CatalogRecord> query, TQuery context)
        where TQuery : QueryContext
    {
        return query;
    }

    /// <summary>
    /// Gets all.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IReadOnlyCollection<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var records = await GetReadQuery().OrderBy(x => x.ItemId).Take(MaxGetAllResults + 1).ToListAsync(cancellationToken);

        if (records.Count > MaxGetAllResults)
        {
            Logger?.LogWarning(
                "GetAllAsync for {EntityType} returned more than {MaxResults} results. The result set has been truncated.",
                typeof(T).Name, MaxGetAllResults);

            records.RemoveAt(records.Count - 1);
        }

        return records.Select(CatalogRecordFactory.Materialize<T>).ToArray();
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

        DbContext.CatalogRecords.Add(CatalogRecordFactory.Create(record));
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
        var existing = await GetTrackedQuery().FirstOrDefaultAsync(x => x.ItemId == record.ItemId, cancellationToken);
        if (existing is null)
        {
            DbContext.CatalogRecords.Add(CatalogRecordFactory.Create(record));
        }
        else
        {
            CatalogRecordFactory.Update(existing, record);
        }
    }

    /// <summary>
    /// Gets read query.
    /// </summary>
    protected IQueryable<CatalogRecord> GetReadQuery()
    {
        return DbContext.CatalogRecords.AsNoTracking().Where(x => x.EntityType == CatalogRecordFactory.GetEntityType<T>());
    }

    /// <summary>
    /// Gets tracked query.
    /// </summary>
    protected IQueryable<CatalogRecord> GetTrackedQuery()
    {
        return DbContext.CatalogRecords.Where(x => x.EntityType == CatalogRecordFactory.GetEntityType<T>());
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
