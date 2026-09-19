using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.DataSources;

/// <summary>
/// Store for managing <see cref="FileSource"/> records.
/// </summary>
public interface IFileSourceStore : ISourceCatalog<FileSource>
{
    /// <summary>
    /// Retrieves every file source that targets the specified AI data source.
    /// </summary>
    /// <param name="dataSourceId">The target AI data source identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The file sources pointing at the data source.</returns>
    Task<IReadOnlyCollection<FileSource>> GetByDataSourceIdAsync(string dataSourceId, CancellationToken cancellationToken = default);
}
