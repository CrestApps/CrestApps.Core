using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

/// <summary>
/// EntityFramework Core-backed <see cref="IFileSourceStore"/>. The connector is the file source's source;
/// the target data source id is denormalized into the catalog record's reference column so file sources can
/// be queried per data source.
/// </summary>
public sealed class EntityCoreFileSourceStore : SourceDocumentCatalog<FileSource>, IFileSourceStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreFileSourceStore"/> class.
    /// </summary>
    /// <param name="dbContext">The db context.</param>
    /// <param name="logger">The logger.</param>
    public EntityCoreFileSourceStore(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<FileSource>> logger = null)
        : base(dbContext, logger)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<FileSource>> GetByDataSourceIdAsync(string dataSourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);

        var records = await GetReadQuery()
            .Where(x => x.ReferenceId == dataSourceId)
            .ToListAsync(cancellationToken);

        return records.Select(CatalogRecordFactory.Materialize<FileSource>).ToArray();
    }
}
