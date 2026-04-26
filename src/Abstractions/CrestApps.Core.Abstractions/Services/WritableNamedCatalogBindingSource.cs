namespace CrestApps.Core.Services;

/// <summary>
/// Wraps an existing <see cref="INamedCatalog{T}"/> as a writable multi-source
/// binding source. Used by persistence layers to expose their DB-backed catalogs
/// as sources for the multi-source store when the model has no source property.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public sealed class WritableNamedCatalogBindingSource<T> : IWritableNamedCatalogSource<T>
    where T : INameAwareModel
{
    private readonly INamedCatalog<T> _inner;

    public WritableNamedCatalogBindingSource(INamedCatalog<T> inner)
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
