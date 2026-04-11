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
    private readonly IWritableNamedCatalogSource<T> _writableSource;

    protected MultiSourceNamedCatalog(IEnumerable<INamedCatalogSource<T>> sources)
    {
        _sources = sources.OrderBy(static source => source.Order);
        _writableSource = sources
            .OfType<IWritableNamedCatalogSource<T>>()
            .OrderBy(static source => source.Order)
            .FirstOrDefault();
    }

    public async ValueTask<IReadOnlyCollection<T>> GetAllAsync()
    {
        return await GetMergedEntriesAsync();
    }

    public async ValueTask<T> FindByIdAsync(string id)
    {
        var entries = await GetMergedEntriesAsync();

        return entries.FirstOrDefault(entry => string.Equals(GetItemId(entry), id, StringComparison.OrdinalIgnoreCase));
    }

    public async ValueTask<T> FindByNameAsync(string name)
    {
        var entries = await GetMergedEntriesAsync();

        return entries.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public async ValueTask<IReadOnlyCollection<T>> GetAsync(IEnumerable<string> ids)
    {
        var idSet = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = await GetMergedEntriesAsync();

        return entries.Where(entry => idSet.Contains(GetItemId(entry))).ToArray();
    }

    public async ValueTask<PageResult<T>> PageAsync<TQuery>(int page, int pageSize, TQuery context)
        where TQuery : QueryContext
    {
        var entries = await GetMergedEntriesAsync();
        var filtered = ApplyFilters(context, entries).ToList();
        var skip = (page - 1) * pageSize;

        return new PageResult<T>
        {
            Count = filtered.Count,
            Entries = filtered.Skip(skip).Take(pageSize).ToArray(),
        };
    }

    public ValueTask<bool> DeleteAsync(T entry)
    {
        EnsureWritableSource();

        return _writableSource.DeleteAsync(entry);
    }

    public ValueTask CreateAsync(T entry)
    {
        EnsureWritableSource();

        return _writableSource.CreateAsync(entry);
    }

    public ValueTask UpdateAsync(T entry)
    {
        EnsureWritableSource();

        return _writableSource.UpdateAsync(entry);
    }

    /// <summary>
    /// Gets the unique identifier for an entry.
    /// Override in derived classes when the identifier is stored in a different property.
    /// </summary>
    protected abstract string GetItemId(T entry);

    /// <summary>
    /// Applies query-context filters to the entries.
    /// Override in derived classes to customize filtering and sorting behavior.
    /// </summary>
    protected virtual IEnumerable<T> ApplyFilters(QueryContext context, IEnumerable<T> entries)
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
    protected virtual string GetSortKey(T entry) => entry.Name;

    private async ValueTask<IReadOnlyCollection<T>> GetMergedEntriesAsync()
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<T>();

        foreach (var source in _sources)
        {
            var entries = await source.GetEntriesAsync(merged);

            foreach (var entry in entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Name) && !seenNames.Add(entry.Name))
                {
                    continue;
                }

                merged.Add(entry);
            }
        }

        return merged;
    }

    private void EnsureWritableSource()
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
