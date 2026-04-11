namespace CrestApps.Core.Services;

/// <summary>
/// Extends <see cref="INamedSourceCatalogSource{T}"/> and <see cref="IWritableNamedCatalogSource{T}"/>
/// with write operations for models that have both name and source. Allows the multi-source
/// catalog to delegate mutations to a persistent source.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface IWritableNamedSourceCatalogSource<T> : INamedSourceCatalogSource<T>, IWritableNamedCatalogSource<T>
    where T : INameAwareModel, ISourceAwareModel
{
}
