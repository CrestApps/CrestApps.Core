namespace CrestApps.Core.Services;

/// <summary>
/// Represents a read-only binding source of catalog entries for models that are identified
/// by both name and source. Extends <see cref="INamedCatalogSource{T}"/> with the additional
/// <see cref="ISourceAwareModel"/> constraint. Each source is ordered by <see cref="Order"/>
/// (lower values have higher priority).
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface INamedSourceCatalogSource<T> : INamedCatalogSource<T>
    where T : INameAwareModel, ISourceAwareModel
{
}
