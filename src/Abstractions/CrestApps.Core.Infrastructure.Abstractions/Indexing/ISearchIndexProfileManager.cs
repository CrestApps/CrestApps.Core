using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.Infrastructure.Indexing;

/// <summary>
/// Manages the full lifecycle of <see cref="SearchIndexProfile"/> entries, including
/// creation, update, deletion, field retrieval, synchronization, and reset operations.
/// Extends <see cref="ICatalogManager{T}"/> with indexing-specific methods.
/// </summary>
public interface ISearchIndexProfileManager : ICatalogManager<SearchIndexProfile>
{
    /// <summary>
    /// Asynchronously finds an index profile by its unique name.
    /// </summary>
    /// <param name="name">The unique name of the profile to find.</param>
    /// <returns>The matching profile, or <see langword="null"/> if not found.</returns>
    ValueTask<SearchIndexProfile> FindByNameAsync(string name);

    /// <summary>
    /// Asynchronously retrieves all index profiles of the specified type.
    /// </summary>
    /// <param name="type">The profile type to filter by (e.g., <see cref="IndexProfileTypes.AIDocuments"/>).</param>
    /// <returns>A read-only collection of profiles matching the specified type.</returns>
    Task<IReadOnlyCollection<SearchIndexProfile>> GetByTypeAsync(string type);

    /// <summary>
    /// Asynchronously retrieves the provider-specific field definitions for the specified profile.
    /// </summary>
    /// <param name="profile">The index profile whose fields should be retrieved.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A read-only collection of field definitions for the profile.</returns>
    ValueTask<IReadOnlyCollection<SearchIndexField>> GetFieldsAsync(
        SearchIndexProfile profile,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously synchronizes the specified profile to its remote search index provider.
    /// Creates the index if it does not exist and updates the schema if it does.
    /// </summary>
    /// <param name="profile">The index profile to synchronize.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task SynchronizeAsync(SearchIndexProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously resets provider-specific state for the specified profile,
    /// preparing it for a full rebuild or re-provisioning.
    /// </summary>
    /// <param name="profile">The index profile to reset.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task ResetAsync(SearchIndexProfile profile, CancellationToken cancellationToken = default);
}
