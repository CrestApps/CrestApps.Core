using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

public sealed class EntityCoreAIMemoryStore : DocumentCatalog<AIMemoryEntry>, IAIMemoryStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreAIMemoryStore"/> class.
    /// </summary>
    /// <param name="dbContext">The db context.</param>
    /// <param name="logger">The logger.</param>
    public EntityCoreAIMemoryStore(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<AIMemoryEntry>> logger = null)
        : base(dbContext, logger)
    {
    }

    /// <summary>
    /// Counts by user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    public Task<int> CountByUserAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);

        return GetReadQuery()
                    .Where(x => x.UserId == userId)
                    .CountAsync();
    }

    /// <summary>
    /// Finds by user and name.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="name">The name.</param>
    public async Task<AIMemoryEntry> FindByUserAndNameAsync(string userId, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var record = await GetReadQuery()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Name == name);

        return record is null ? null : CatalogRecordFactory.Materialize<AIMemoryEntry>(record);
    }

    /// <summary>
    /// Gets by user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="limit">The limit.</param>
    public async Task<IReadOnlyCollection<AIMemoryEntry>> GetByUserAsync(string userId, int limit = 100)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);

        var records = await GetReadQuery()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .Take(limit)
            .ToListAsync();

        return records
                    .Select(CatalogRecordFactory.Materialize<AIMemoryEntry>)
                    .ToArray();
    }
}
