namespace CrestApps.Core.Services;

/// <summary>
/// Represents a read-only binding source of catalog entries for models that are identified
/// by name. Each source is ordered by <see cref="Order"/> (lower values have higher priority).
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface INamedCatalogSource<T>
    where T : INameAwareModel
{
    /// <summary>
    /// Gets the priority order of this source. Lower values indicate higher priority.
    /// When entries with the same name exist in multiple sources, the source with the
    /// lower order value wins.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Asynchronously retrieves all entries provided by this source.
    /// </summary>
    /// <param name="knownEntries">
    /// Entries already collected from higher-priority sources, allowing this source
    /// to skip entries whose names conflict with existing ones.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A read-only collection of entries from this source.</returns>
    ValueTask<IReadOnlyCollection<T>> GetEntriesAsync(IReadOnlyCollection<T> knownEntries, CancellationToken cancellationToken = default);
}
