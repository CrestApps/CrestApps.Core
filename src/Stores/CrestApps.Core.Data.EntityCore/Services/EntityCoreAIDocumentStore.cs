using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

public sealed class EntityCoreAIDocumentStore : DocumentCatalog<AIDocument>, IAIDocumentStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreAIDocumentStore"/> class.
    /// </summary>
    /// <param name="dbContext">The db context.</param>
    /// <param name="logger">The logger.</param>
    public EntityCoreAIDocumentStore(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<AIDocument>> logger = null)
        : base(dbContext, logger)
    {
    }

    /// <summary>
    /// Gets documents.
    /// </summary>
    /// <param name="referenceId">The reference id.</param>
    /// <param name="referenceType">The reference type.</param>
    public async Task<IReadOnlyCollection<AIDocument>> GetDocumentsAsync(string referenceId, string referenceType)
    {
        ArgumentException.ThrowIfNullOrEmpty(referenceId);
        ArgumentException.ThrowIfNullOrEmpty(referenceType);

        var records = await GetReadQuery()
            .Where(x => x.ReferenceId == referenceId && x.ReferenceType == referenceType)
            .ToListAsync();

return records
            .Select(CatalogRecordFactory.Materialize<AIDocument>)
            .ToArray();
    }
}
