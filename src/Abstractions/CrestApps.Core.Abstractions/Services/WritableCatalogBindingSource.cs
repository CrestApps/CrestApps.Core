namespace CrestApps.Core.Services;

/// <summary>
/// Wraps an existing <see cref="INamedSourceCatalog{T}"/> as a writable multi-source
/// binding source. Used by persistence layers (YesSql, EntityCore) to expose their
/// DB-backed catalogs as sources for the multi-source store.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public sealed class WritableCatalogBindingSource<T> : IWritableNamedSourceCatalogSource<T>
    where T : INameAwareModel, ISourceAwareModel
{
    private readonly INamedSourceCatalog<T> _inner;

    /// <summary>
    /// Initializes a new instance of the <see cref="WritableCatalogBindingSource"/> class.
    /// </summary>
    /// <param name="inner">The inner catalog.</param>
    public WritableCatalogBindingSource(INamedSourceCatalog<T> inner)
    {
        _inner = inner;
    }

    /// <summary>
    /// Gets the priority order. DB-backed sources use 0 (highest priority).
    /// </summary>
    public int Order => 0;

    /// <summary>
    /// Gets entries.
    /// </summary>
    /// <param name="knownEntries">The known entries.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<IReadOnlyCollection<T>> GetEntriesAsync(IReadOnlyCollection<T> knownEntries, CancellationToken cancellationToken = default)
        => _inner.GetAllAsync(cancellationToken);

    /// <summary>
    /// Deletes the operation.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<bool> DeleteAsync(T entry, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(entry, cancellationToken);

    /// <summary>
    /// Creates the operation.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask CreateAsync(T entry, CancellationToken cancellationToken = default)
        => _inner.CreateAsync(entry, cancellationToken);

    /// <summary>
    /// Updates the operation.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask UpdateAsync(T entry, CancellationToken cancellationToken = default)
        => _inner.UpdateAsync(entry, cancellationToken);
}
