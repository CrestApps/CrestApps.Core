using CrestApps.Core.Models;

namespace CrestApps.Core.Services;

/// <summary>
/// Provides read-only management access to catalog entries with handler invocation,
/// supporting retrieval by ID, bulk listing, and paginated queries.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface IReadCatalogManager<T>
{
    /// <summary>
    /// Asynchronously finds a catalog entry by its unique identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the entry.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The matching entry, or <see langword="null"/> if not found.</returns>
    ValueTask<T> FindByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves all entries in the catalog.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>An enumerable of all catalog entries.</returns>
    ValueTask<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves a paginated subset of catalog entries using the specified query context.
    /// </summary>
    /// <typeparam name="TQuery">The query context type used to filter and order results.</typeparam>
    /// <param name="page">The one-based page number to retrieve.</param>
    /// <param name="pageSize">The number of entries per page.</param>
    /// <param name="context">The query context used to filter and order results.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A page result containing the entries and total count.</returns>
    ValueTask<PageResult<T>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
        where TQuery : QueryContext;
}
