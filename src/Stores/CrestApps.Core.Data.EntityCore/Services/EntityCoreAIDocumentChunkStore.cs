using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

public sealed class EntityCoreAIDocumentChunkStore : DocumentCatalog<AIDocumentChunk>, IAIDocumentChunkStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreAIDocumentChunkStore"/> class.
    /// </summary>
    /// <param name="dbContext">The db context.</param>
    /// <param name="logger">The logger.</param>
    public EntityCoreAIDocumentChunkStore(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<AIDocumentChunk>> logger = null)
        : base(dbContext, logger)
    {
    }

    /// <summary>
    /// Gets chunks by ai document id.
    /// </summary>
    /// <param name="documentId">The document id.</param>
    public async Task<IReadOnlyCollection<AIDocumentChunk>> GetChunksByAIDocumentIdAsync(string documentId)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId);

        var records = await GetReadQuery()
            .Where(x => x.AIDocumentId == documentId)
            .ToListAsync();

        return records
            .Select(CatalogRecordFactory.Materialize<AIDocumentChunk>)
            .ToArray();
    }

    /// <summary>
    /// Gets chunks by reference.
    /// </summary>
    /// <param name="referenceId">The reference id.</param>
    /// <param name="referenceType">The reference type.</param>
    public async Task<IReadOnlyCollection<AIDocumentChunk>> GetChunksByReferenceAsync(string referenceId, string referenceType)
    {
        ArgumentException.ThrowIfNullOrEmpty(referenceId);
        ArgumentException.ThrowIfNullOrEmpty(referenceType);

        var records = await GetReadQuery()
            .Where(x => x.ReferenceId == referenceId && x.ReferenceType == referenceType)
            .ToListAsync();

        return records
            .Select(CatalogRecordFactory.Materialize<AIDocumentChunk>)
            .ToArray();
    }

    /// <summary>
    /// Deletes by document id.
    /// </summary>
    /// <param name="documentId">The document id.</param>
    public async Task DeleteByDocumentIdAsync(string documentId)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId);

        var records = await GetTrackedQuery()
            .Where(x => x.AIDocumentId == documentId)
            .ToListAsync();

        if (records.Count == 0)
        {
            return;
        }

        DbContext.CatalogRecords.RemoveRange(records);
    }
}
