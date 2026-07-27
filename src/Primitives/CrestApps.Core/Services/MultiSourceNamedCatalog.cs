using CrestApps.Core.Models;

namespace CrestApps.Core.Services;

/// <summary>
/// A base class that aggregates entries from multiple <see cref="INamedCatalogSource{T}"/>
/// implementations, deduplicating by name (lower-order sources win). Write operations are
/// delegated to the first <see cref="IWritableNamedCatalogSource{T}"/> found.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public abstract class MultiSourceNamedCatalog<T> : INamedCatalog<T>
    where T : INameAwareModel
{
    private readonly IEnumerable<INamedCatalogSource<T>> _sources;
    private readonly IWritableNamedCatalogSource<T>? _writableSource;
    private IReadOnlyCollection<T>? _cachedEntries;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiSourceNamedCatalog"/> class.
    /// </summary>
    /// <param name="sources">The sources.</param>
    protected MultiSourceNamedCatalog(IEnumerable<INamedCatalogSource<T>> sources)
    {
        _sources = sources.OrderBy(static source => source.Order);
        _writableSource = sources
            .OfType<IWritableNamedCatalogSource<T>>()
            .OrderBy(static source => source.Order)
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets all.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IReadOnlyCollection<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await GetMergedEntriesAsync(cancellationToken);
    }

    /// <summary>
    /// Finds by id.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<T> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var entries = await GetMergedEntriesAsync(cancellationToken);

        return entries.FirstOrDefault(entry => string.Equals(GetItemId(entry), id, StringComparison.OrdinalIgnoreCase))!;
    }

    /// <summary>
    /// Finds by name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<T> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var entries = await GetMergedEntriesAsync(cancellationToken);

        return entries.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))!;
    }

    /// <summary>
    /// Gets the operation.
    /// </summary>
    /// <param name="ids">The ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IReadOnlyCollection<T>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var idSet = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = await GetMergedEntriesAsync(cancellationToken);

        return entries.Where(entry => idSet.Contains(GetItemId(entry))).ToArray();
    }

    /// <summary>
    /// Pages the operation.
    /// </summary>
    public async ValueTask<PageResult<T>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
        where TQuery : QueryContext
    {
        var entries = await GetMergedEntriesAsync(cancellationToken);
        var skip = (page - 1) * pageSize;

        if (CanUseDirectPaging(context))
        {
            return new PageResult<T>
            {
                Count = entries.Count,
                Entries = GetPageEntries(entries, skip, pageSize),
            };
        }

        var pageEntries = new List<T>(Math.Clamp(pageSize, 0, entries.Count));
        var count = 0;

        foreach (var entry in ApplyFilters(context, entries))
        {
            if (count >= skip && pageEntries.Count < pageSize)
            {
                pageEntries.Add(entry);
            }

            count++;
        }

        return new PageResult<T>
        {
            Count = count,
            Entries = pageEntries.ToArray(),
        };
    }

    private static bool CanUseDirectPaging(QueryContext? context)
    {
        return context is null ||
            (context.GetType() == typeof(QueryContext) &&
             string.IsNullOrEmpty(context.Source) &&
             string.IsNullOrEmpty(context.Name) &&
             !context.Sorted);
    }

    private static T[] GetPageEntries(
        IReadOnlyCollection<T> entries,
        int skip,
        int pageSize)
    {
        if (pageSize <= 0)
        {
            return [];
        }

        var start = Math.Max(0, skip);

        if (start >= entries.Count)
        {
            return [];
        }

        var length = Math.Min(pageSize, entries.Count - start);

        if (entries is T[] array)
        {
            return array.AsSpan(start, length).ToArray();
        }

        if (entries is IReadOnlyList<T> list)
        {
            var pageEntries = new T[length];

            for (var i = 0; i < length; i++)
            {
                pageEntries[i] = list[start + i];
            }

            return pageEntries;
        }

        return entries.Skip(start).Take(length).ToArray();
    }

    /// <summary>
    /// Deletes the operation.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<bool> DeleteAsync(T entry, CancellationToken cancellationToken = default)
    {
        EnsureWritableSource();

        var deleted = await _writableSource!.DeleteAsync(entry, cancellationToken);

        if (deleted)
        {
            InvalidateCache();
        }

        return deleted;
    }

    /// <summary>
    /// Creates the operation.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask CreateAsync(T entry, CancellationToken cancellationToken = default)
    {
        EnsureWritableSource();

        await _writableSource!.CreateAsync(entry, cancellationToken);

        InvalidateCache();
    }

    /// <summary>
    /// Updates the operation.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask UpdateAsync(T entry, CancellationToken cancellationToken = default)
    {
        EnsureWritableSource();

        await _writableSource!.UpdateAsync(entry, cancellationToken);

        InvalidateCache();
    }

    /// <summary>
    /// Gets the unique identifier for an entry.
    /// Override in derived classes when the identifier is stored in a different property.
    /// </summary>
    /// <param name="entry">The entry.</param>
    protected abstract string GetItemId(T entry);

    /// <summary>
    /// Applies query-context filters to the entries.
    /// Override in derived classes to customize filtering and sorting behavior.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="entries">The entries.</param>
    protected virtual IEnumerable<T> ApplyFilters(QueryContext? context, IEnumerable<T> entries)
    {
        if (context is null)
        {
            return entries;
        }

        if (!string.IsNullOrEmpty(context.Name))
        {
            entries = entries.Where(entry => entry.Name.Contains(context.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (context.Sorted)
        {
            entries = entries.OrderBy(GetSortKey, StringComparer.OrdinalIgnoreCase);
        }

        return entries;
    }

    /// <summary>
    /// Returns the sort key for an entry. Defaults to <see cref="INameAwareModel.Name"/>.
    /// Override in derived classes to customize sort order.
    /// </summary>
    /// <param name="entry">The entry.</param>
    protected virtual string GetSortKey(T entry) => entry.Name;

    /// <summary>
    /// Collects entries from all sources, deduplicating by name (lower-order sources win).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected async ValueTask<IReadOnlyCollection<T>> GetMergedEntriesAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedEntries is not null)
        {
            return _cachedEntries;
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<T>();

        foreach (var source in _sources)
        {
            var entries = await source.GetEntriesAsync(merged, cancellationToken);

            foreach (var entry in entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Name) && !seenNames.Add(entry.Name))
                {
                    continue;
                }

                merged.Add(entry);
            }
        }

        _cachedEntries = merged.ToArray();

        return _cachedEntries;
    }

    /// <summary>
    /// Invalidates the scoped merged-entry cache so subsequent reads observe writes.
    /// </summary>
    protected virtual void InvalidateCache()
    {
        _cachedEntries = null;
    }

    /// <summary>
    /// Throws an <see cref="InvalidOperationException"/> when no writable source is registered.
    /// </summary>
    protected void EnsureWritableSource()
    {
        if (_writableSource is null)
        {
            throw new InvalidOperationException(
                $"""
                No writable source is registered for {typeof(T).Name}.
                Register an {nameof(IWritableNamedCatalogSource<>)} implementation to enable write operations.
                """);
        }
    }
}
