using System.Text.Json.Nodes;
using CrestApps.Core.Models;

namespace CrestApps.Core.Services;

/// <summary>
/// A catalog manager that supports composite lookup by both name and source,
/// extending <see cref="IReadCatalogManager{T}"/>
/// for models that implement both <see cref="INameAwareModel"/> and <see cref="ISourceAwareModel"/>.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface INamedSourceCatalogManager<T> : IReadCatalogManager<T>
    where T : INameAwareModel, ISourceAwareModel
{
    /// <summary>
    /// Asynchronously deletes the specified model from the catalog.
    /// </summary>
    /// <param name="model">The model to delete.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><see langword="true"/> if the model was successfully deleted; otherwise, <see langword="false"/>.</returns>
    ValueTask<bool> DeleteAsync(T model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously creates a new model instance pre-assigned to the specified name and source,
    /// optionally populating it from JSON data.
    /// </summary>
    /// <param name="name">The name to assign to the new model.</param>
    /// <param name="source">The source to assign to the new model.</param>
    /// <param name="data">Optional JSON data to seed the new model.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A newly created and initialized model instance assigned to the specified name and source.</returns>
    ValueTask<T> NewAsync(string name, string source, JsonNode data = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously creates the specified model in the catalog.
    /// </summary>
    /// <param name="model">The model to create.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    ValueTask CreateAsync(T model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously updates the specified model in the catalog, optionally merging changes from JSON data.
    /// </summary>
    /// <param name="model">The model to update.</param>
    /// <param name="data">Optional JSON data containing fields to merge into the model.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    ValueTask UpdateAsync(T model, JsonNode data = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously validates the specified model and returns the validation result.
    /// </summary>
    /// <param name="model">The model to validate.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The validation result details indicating success or failure with error messages.</returns>
    ValueTask<ValidationResultDetails> ValidateAsync(T model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously finds a catalog entry by its unique name.
    /// </summary>
    /// <param name="name">The unique name of the entry to find.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The matching entry, or <see langword="null"/> if no entry with the specified name exists.</returns>
    ValueTask<T> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves all catalog entries belonging to the specified source.
    /// </summary>
    /// <param name="source">The source or provider name to filter by.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>An enumerable of entries matching the specified source.</returns>
    ValueTask<IEnumerable<T>> GetAsync(string source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously finds all catalog entries that belong to the specified source.
    /// </summary>
    /// <param name="source">The source or provider name to search for.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>An enumerable of entries matching the specified source.</returns>
    ValueTask<IEnumerable<T>> FindBySourceAsync(string source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves a catalog entry by its unique name and source combination.
    /// </summary>
    /// <param name="name">The unique name of the entry.</param>
    /// <param name="source">The source or provider name of the entry.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The matching entry, or <see langword="null"/> if not found.</returns>
    ValueTask<T> GetAsync(string name, string source, CancellationToken cancellationToken = default);
}
