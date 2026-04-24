using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.Infrastructure.Indexing;

/// <summary>
/// Store for managing <see cref="SearchIndexProfile"/> records.
/// </summary>
public interface ISearchIndexProfileStore : ICatalog<SearchIndexProfile>, INamedCatalog<SearchIndexProfile>
{
    /// <summary>
    /// Gets all index profiles of the specified type (e.g., "AIDocuments", "DataSourceIndex", "AIMemory").
    /// </summary>
    /// <param name="type">The index profile type to filter by.</param>
    /// <returns>A read-only collection of matching index profiles.</returns>
    Task<IReadOnlyCollection<SearchIndexProfile>> GetByTypeAsync(string type);
}
