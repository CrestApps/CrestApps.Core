using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

public sealed class EntityCoreAIDocumentChunkStore : DocumentCatalog<AIDocumentChunk>, IAIDocumentChunkStore
{
    public EntityCoreAIDocumentChunkStore(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<AIDocumentChunk>> logger = null)
        : base(dbContext, logger)
    {
    }

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
