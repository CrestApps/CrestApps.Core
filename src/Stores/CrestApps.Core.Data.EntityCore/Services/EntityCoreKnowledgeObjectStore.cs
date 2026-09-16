using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

/// <summary>
/// EntityFramework Core-backed <see cref="IKnowledgeObjectStore"/>. Records are grouped by the owning data
/// source, stored as their source.
/// </summary>
/// <remarks>
/// The record factory denormalizes the canonical identifier into the record's name, the root identifier into
/// its reference identifier, the object type into its reference type and the content hash into its own
/// column, so every lookup here filters in the database rather than materializing a data source's worth of
/// objects to pick one out.
/// </remarks>
public sealed class EntityCoreKnowledgeObjectStore : SourceDocumentCatalog<KnowledgeObject>, IKnowledgeObjectStore
{
    private static readonly string[] _figureTypes = [KnowledgeContentTypes.Figure, KnowledgeContentTypes.Chart];

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreKnowledgeObjectStore"/> class.
    /// </summary>
    /// <param name="dbContext">The db context.</param>
    /// <param name="logger">The logger.</param>
    public EntityCoreKnowledgeObjectStore(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<KnowledgeObject>> logger = null)
        : base(dbContext, logger)
    {
    }

    /// <inheritdoc />
    public async Task<KnowledgeObject> FindByCanonicalIdAsync(string dataSourceId, string canonicalId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);
        ArgumentException.ThrowIfNullOrEmpty(canonicalId);

        var record = await GetReadQuery()
            .Where(x => x.Source == dataSourceId && x.Name == canonicalId)
            .FirstOrDefaultAsync(cancellationToken);

        return record is null ? null : CatalogRecordFactory.Materialize<KnowledgeObject>(record);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<KnowledgeObject>> GetByCanonicalIdsAsync(string dataSourceId, IEnumerable<string> canonicalIds, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);
        ArgumentNullException.ThrowIfNull(canonicalIds);

        var wanted = canonicalIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (wanted.Length == 0)
        {
            return [];
        }

        var records = await GetReadQuery()
            .Where(x => x.Source == dataSourceId && wanted.Contains(x.Name))
            .ToListAsync(cancellationToken);

        return records.Select(CatalogRecordFactory.Materialize<KnowledgeObject>).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<KnowledgeObject>> GetByRootIdAsync(string dataSourceId, string rootId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);
        ArgumentException.ThrowIfNullOrEmpty(rootId);

        var records = await GetReadQuery()
            .Where(x => x.Source == dataSourceId && x.ReferenceId == rootId)
            .ToListAsync(cancellationToken);

        return records.Select(CatalogRecordFactory.Materialize<KnowledgeObject>).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<KnowledgeObject>> GetByDataSourceIdAsync(string dataSourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);

        return await GetAsync(dataSourceId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<KnowledgeObject>> GetByStatusAsync(string dataSourceId, string status, int take, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);
        ArgumentException.ThrowIfNullOrEmpty(status);

        // The status lives only in the document payload, so the data source is narrowed in the database and
        // the status is read off the materialized objects, oldest first so nothing waits forever.
        var objects = await GetAsync(dataSourceId, cancellationToken);

        return objects
            .Where(entry => string.Equals(entry.Status, status, StringComparison.Ordinal))
            .OrderBy(entry => entry.CreatedUtc)
            .Take(take < 1 ? 1 : take)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<KnowledgeObject>> GetByObjectTypesAsync(string dataSourceId, IEnumerable<string> objectTypes, int take, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);

        var wanted = objectTypes?
            .Where(objectType => !string.IsNullOrWhiteSpace(objectType))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];

        // The object type is denormalized into the record's reference type, so both the kinds and the bound
        // are applied by the database. A listing has no query to rank against, which is precisely why it must
        // never become a scan of the data source: it is the one call nothing else narrows.
        var query = GetReadQuery().Where(x => x.Source == dataSourceId);

        if (wanted.Length > 0)
        {
            query = query.Where(x => wanted.Contains(x.ReferenceType));
        }

        // Ordered by the canonical identifier, which is the record's name, so a bounded listing returns the
        // same objects every time rather than whatever the database happened to hand back first.
        var records = await query
            .OrderBy(x => x.Name)
            .Take(take < 1 ? 1 : take)
            .ToListAsync(cancellationToken);

        return records.Select(CatalogRecordFactory.Materialize<KnowledgeObject>).ToArray();
    }

    /// <inheritdoc />
    public async Task<KnowledgeObject> FindFigureByContentHashAsync(string contentHash, string promptVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentHash);

        // The lookup crosses data sources on purpose: the same artwork is the same artwork wherever it was
        // ingested, and transcribing it twice buys nothing. Only figures are read, because only a figure can
        // carry a description. Both the hash and the object type are denormalized onto the record, so the
        // database returns the handful of objects made from these bytes rather than every figure in the
        // installation once per pending figure.
        var records = await GetReadQuery()
            .Where(x => x.ContentHash == contentHash && _figureTypes.Contains(x.ReferenceType))
            .ToListAsync(cancellationToken);

        foreach (var record in records)
        {
            var entry = CatalogRecordFactory.Materialize<KnowledgeObject>(record);

            if (IsDescribedFigure(entry, contentHash, promptVersion))
            {
                return entry;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task DeleteByRootIdAsync(string dataSourceId, string rootId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);
        ArgumentException.ThrowIfNullOrEmpty(rootId);

        var records = await GetTrackedQuery()
            .Where(x => x.Source == dataSourceId && x.ReferenceId == rootId)
            .ToListAsync(cancellationToken);

        foreach (var record in records)
        {
            if (record.Document is not null)
            {
                DbContext.Documents.Remove(record.Document);
            }

            DbContext.CatalogRecords.Remove(record);
        }
    }

    /// <summary>
    /// Determines whether an object is a figure already transcribed from the same bytes by the same prompt.
    /// </summary>
    /// <param name="entry">The object.</param>
    /// <param name="contentHash">The hash of the bytes.</param>
    /// <param name="promptVersion">The transcription prompt version.</param>
    /// <returns><see langword="true"/> when the description can be reused.</returns>
    /// <remarks>
    /// The type and the hash are re-checked against the payload even though the query already narrowed on the
    /// denormalized columns, because the database compares strings with its own collation and a
    /// case-insensitive one would hand back bytes that are not these bytes.
    /// </remarks>
    private static bool IsDescribedFigure(KnowledgeObject entry, string contentHash, string promptVersion)
    {
        if (entry.ObjectType is not (KnowledgeContentTypes.Figure or KnowledgeContentTypes.Chart))
        {
            return false;
        }

        if (!string.Equals(entry.ContentHash, contentHash, StringComparison.Ordinal))
        {
            return false;
        }

        if (!entry.TryGet<FigureDetails>(out var details) || string.IsNullOrWhiteSpace(details.Description))
        {
            return false;
        }

        return string.IsNullOrEmpty(promptVersion) ||
            string.Equals(details.DescriptionPromptVersion, promptVersion, StringComparison.Ordinal);
    }
}
