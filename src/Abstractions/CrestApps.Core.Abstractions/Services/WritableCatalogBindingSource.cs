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

    public WritableCatalogBindingSource(INamedSourceCatalog<T> inner)
    {
        _inner = inner;
    }

    /// <summary>
    /// Gets the priority order. DB-backed sources use 0 (highest priority).
    /// </summary>
    public int Order => 0;

    public ValueTask<IReadOnlyCollection<T>> GetEntriesAsync(IReadOnlyCollection<T> knownEntries, CancellationToken cancellationToken = default)
        => _inner.GetAllAsync(cancellationToken);

    public ValueTask<bool> DeleteAsync(T entry, CancellationToken cancellationToken = default)
        => _inner.DeleteAsync(entry, cancellationToken);

    public ValueTask CreateAsync(T entry, CancellationToken cancellationToken = default)
        => _inner.CreateAsync(entry, cancellationToken);

    public ValueTask UpdateAsync(T entry, CancellationToken cancellationToken = default)
        => _inner.UpdateAsync(entry, cancellationToken);
}
