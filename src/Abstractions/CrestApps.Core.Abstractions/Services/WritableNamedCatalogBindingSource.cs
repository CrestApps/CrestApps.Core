namespace CrestApps.Core.Services;

/// <summary>
/// Wraps an existing <see cref="INamedCatalog{T}"/> as a writable multi-source
/// binding source. Used by persistence layers to expose their DB-backed catalogs
/// as sources for the multi-source store when the model has no source property.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public class WritableNamedCatalogBindingSource<T> : IWritableNamedCatalogSource<T>
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

    public ValueTask<IReadOnlyCollection<T>> GetEntriesAsync(IReadOnlyCollection<T> knownEntries)
        => _inner.GetAllAsync();

    public ValueTask<bool> DeleteAsync(T entry)
        => _inner.DeleteAsync(entry);

    public ValueTask CreateAsync(T entry)
        => _inner.CreateAsync(entry);

    public ValueTask UpdateAsync(T entry)
        => _inner.UpdateAsync(entry);
}
