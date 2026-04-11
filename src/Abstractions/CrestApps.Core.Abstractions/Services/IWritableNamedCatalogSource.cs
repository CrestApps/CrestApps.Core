namespace CrestApps.Core.Services;

/// <summary>
/// Extends <see cref="INamedCatalogSource{T}"/> with write operations
/// (create, update, delete), allowing the multi-source catalog to delegate
/// mutations to a persistent source.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface IWritableNamedCatalogSource<T> : INamedCatalogSource<T>
    where T : INameAwareModel
{
    /// <summary>
    /// Asynchronously deletes the specified entry from this source.
    /// </summary>
    /// <param name="entry">The entry to delete.</param>
    /// <returns><see langword="true"/> if the entry was successfully deleted; otherwise, <see langword="false"/>.</returns>
    ValueTask<bool> DeleteAsync(T entry);

    /// <summary>
    /// Asynchronously creates the specified entry in this source.
    /// </summary>
    /// <param name="entry">The entry to create.</param>
    ValueTask CreateAsync(T entry);

    /// <summary>
    /// Asynchronously updates the specified entry in this source.
    /// </summary>
    /// <param name="entry">The entry to update.</param>
    ValueTask UpdateAsync(T entry);
}
