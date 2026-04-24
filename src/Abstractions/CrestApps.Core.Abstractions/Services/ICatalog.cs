namespace CrestApps.Core.Services;

/// <summary>
/// Provides full CRUD operations for catalog entries, extending read-only access
/// with the ability to create, update, and delete entries.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalog<T> : IReadCatalog<T>
{
    /// <summary>
    /// Asynchronously deletes the specified entry from the catalog.
    /// </summary>
    /// <param name="entry">The entry to delete.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> if the entry was successfully deleted; otherwise, <see langword="false"/>.</returns>
    ValueTask<bool> DeleteAsync(T entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously creates the specified entry in the catalog.
    /// </summary>
    /// <param name="entry">The entry to create.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    ValueTask CreateAsync(T entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously updates the specified entry in the catalog.
    /// </summary>
    /// <param name="entry">The entry to update.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    ValueTask UpdateAsync(T entry, CancellationToken cancellationToken = default);

}
