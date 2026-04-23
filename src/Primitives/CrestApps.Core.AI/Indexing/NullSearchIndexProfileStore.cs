using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;

namespace CrestApps.Core.AI.Indexing;

/// <summary>
/// Fallback in-memory no-op store used when a host has not registered a persistent
/// <see cref="ISearchIndexProfileStore"/> implementation yet.
/// </summary>
public sealed class NullSearchIndexProfileStore : ISearchIndexProfileStore
{
    public ValueTask<SearchIndexProfile> FindByIdAsync(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        return ValueTask.FromResult<SearchIndexProfile>(default);
    }

    public ValueTask<IReadOnlyCollection<SearchIndexProfile>> GetAllAsync()
    {
        return ValueTask.FromResult<IReadOnlyCollection<SearchIndexProfile>>([]);
    }

    public ValueTask<IReadOnlyCollection<SearchIndexProfile>> GetAsync(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        return ValueTask.FromResult<IReadOnlyCollection<SearchIndexProfile>>([]);
    }

    public ValueTask<PageResult<SearchIndexProfile>> PageAsync<TQuery>(int page, int pageSize, TQuery context)
        where TQuery : QueryContext
    {
        ArgumentNullException.ThrowIfNull(context);

        return ValueTask.FromResult(new PageResult<SearchIndexProfile>
        {
            Count = 0,
            Entries = [],
        });
    }

    public ValueTask<bool> DeleteAsync(SearchIndexProfile entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return ValueTask.FromResult(false);
    }

    public ValueTask CreateAsync(SearchIndexProfile entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(SearchIndexProfile entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return ValueTask.CompletedTask;
    }

    public ValueTask<SearchIndexProfile> FindByNameAsync(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return ValueTask.FromResult<SearchIndexProfile>(default);
    }

    public Task<IReadOnlyCollection<SearchIndexProfile>> GetByTypeAsync(string type)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);

        return Task.FromResult<IReadOnlyCollection<SearchIndexProfile>>([]);
    }
}
