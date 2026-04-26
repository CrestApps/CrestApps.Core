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
    /// <summary>
    /// Find by ids by id.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<SearchIndexProfile> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        return ValueTask.FromResult<SearchIndexProfile>(default);
    }

    /// <summary>
    /// Gets all.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<IReadOnlyCollection<SearchIndexProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IReadOnlyCollection<SearchIndexProfile>>([]);
    }

    /// <summary>
    /// Gets the operation.
    /// </summary>
    /// <param name="ids">The ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<IReadOnlyCollection<SearchIndexProfile>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        return ValueTask.FromResult<IReadOnlyCollection<SearchIndexProfile>>([]);
    }

    /// <summary>
    /// Pages the operation.
    /// </summary>
    public ValueTask<PageResult<SearchIndexProfile>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
        where TQuery : QueryContext
    {
        ArgumentNullException.ThrowIfNull(context);

        return ValueTask.FromResult(new PageResult<SearchIndexProfile>
        {
            Count = 0,
            Entries = [],
        });
    }

    /// <summary>
    /// Deletes the operation.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<bool> DeleteAsync(SearchIndexProfile entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return ValueTask.FromResult(false);
    }

    /// <summary>
    /// Creates the operation.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask CreateAsync(SearchIndexProfile entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Updates the operation.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask UpdateAsync(SearchIndexProfile entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Finds by name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<SearchIndexProfile> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return ValueTask.FromResult<SearchIndexProfile>(default);
    }

    /// <summary>
    /// Gets by type.
    /// </summary>
    /// <param name="type">The type.</param>
    public Task<IReadOnlyCollection<SearchIndexProfile>> GetByTypeAsync(string type)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);

        return Task.FromResult<IReadOnlyCollection<SearchIndexProfile>>([]);
    }
}
