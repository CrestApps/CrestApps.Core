using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

public sealed class EntityCoreAIChatSessionPromptStore : DocumentCatalog<AIChatSessionPrompt>, IAIChatSessionPromptStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreAIChatSessionPromptStore"/> class.
    /// </summary>
    /// <param name="dbContext">The db context.</param>
    /// <param name="logger">The logger.</param>
    public EntityCoreAIChatSessionPromptStore(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<AIChatSessionPrompt>> logger = null)
        : base(dbContext, logger)
    {
    }

    /// <summary>
    /// Gets prompts.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    public async Task<IReadOnlyList<AIChatSessionPrompt>> GetPromptsAsync(string sessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var records = await GetReadQuery()
            .Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync();

        return records
                    .Select(CatalogRecordFactory.Materialize<AIChatSessionPrompt>)
                    .ToArray();
    }

    /// <summary>
    /// Deletes all prompts.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    public async Task<int> DeleteAllPromptsAsync(string sessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var records = await GetTrackedQuery()
            .Where(x => x.SessionId == sessionId)
            .ToListAsync();

        if (records.Count == 0)
        {
            return 0;
        }

        DbContext.CatalogRecords.RemoveRange(records);

        return records.Count;
    }

    /// <summary>
    /// Counts the operation.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    public Task<int> CountAsync(string sessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        return GetReadQuery()
                    .Where(x => x.SessionId == sessionId)
                    .CountAsync();
    }
}
