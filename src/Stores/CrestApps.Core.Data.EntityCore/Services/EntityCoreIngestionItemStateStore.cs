using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

/// <summary>
/// EntityFramework Core-backed <see cref="IIngestionItemStateStore"/>. Records are grouped by the owning
/// record, stored as their source.
/// </summary>
public sealed class EntityCoreIngestionItemStateStore : SourceDocumentCatalog<IngestionItemState>, IIngestionItemStateStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreIngestionItemStateStore"/> class.
    /// </summary>
    /// <param name="dbContext">The db context.</param>
    /// <param name="logger">The logger.</param>
    public EntityCoreIngestionItemStateStore(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<IngestionItemState>> logger = null)
        : base(dbContext, logger)
    {
    }

    /// <inheritdoc />
    public async Task DeleteBySourceIdAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceId);

        var records = await GetTrackedQuery()
            .Where(x => x.Source == sourceId)
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
        {
            return;
        }

        foreach (var record in records)
        {
            if (record.Document is not null)
            {
                DbContext.Documents.Remove(record.Document);
            }
        }

        DbContext.CatalogRecords.RemoveRange(records);
    }

    /// <inheritdoc />
    public async Task DeleteByItemKeysAsync(string sourceId, IEnumerable<string> itemKeys, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceId);
        ArgumentNullException.ThrowIfNull(itemKeys);

        var keys = new HashSet<string>(itemKeys.Where(key => !string.IsNullOrWhiteSpace(key)), StringComparer.OrdinalIgnoreCase);

        if (keys.Count == 0)
        {
            return;
        }

        var records = await GetTrackedQuery()
            .Where(x => x.Source == sourceId)
            .ToListAsync(cancellationToken);

        foreach (var record in records)
        {
            var state = CatalogRecordFactory.Materialize<IngestionItemState>(record);

            if (!keys.Contains(state.ItemKey))
            {
                continue;
            }

            if (record.Document is not null)
            {
                DbContext.Documents.Remove(record.Document);
            }

            DbContext.CatalogRecords.Remove(record);
        }
    }
}
