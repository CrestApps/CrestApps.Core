using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.EntityCore.Models;
using Microsoft.EntityFrameworkCore;

namespace CrestApps.Core.Data.EntityCore.Services;

/// <summary>
/// Entity Framework Core-backed store for AI completion usage records.
/// </summary>
public sealed class EntityCoreAICompletionUsageStore : IAICompletionUsageStore
{
    private readonly CrestAppsEntityDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreAICompletionUsageStore"/> class.
    /// </summary>
    /// <param name="dbContext">The Entity Framework Core database context.</param>
    public EntityCoreAICompletionUsageStore(CrestAppsEntityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Saves a usage record.
    /// </summary>
    /// <param name="record">The usage record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task SaveAsync(
        AICompletionUsageRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        _dbContext.AICompletionUsageRecords.Add(new AICompletionUsageStoreRecord
        {
            Document = new DocumentRecord
            {
                Type = typeof(AICompletionUsageRecord).FullName!,
                Content = EntityCoreStoreSerializer.Serialize(record),
            },
            CreatedUtc = record.CreatedUtc,
            SessionId = record.SessionId,
            InteractionId = record.InteractionId,
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Retrieves usage records captured within the optional UTC date range.
    /// </summary>
    /// <param name="startDateUtc">The inclusive UTC start date filter.</param>
    /// <param name="endDateUtc">The inclusive UTC end date filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<IReadOnlyList<AICompletionUsageRecord>> GetAsync(
        DateTime? startDateUtc,
        DateTime? endDateUtc,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.AICompletionUsageRecords
            .AsNoTracking()
            .Include(x => x.Document)
            .AsQueryable();

        if (startDateUtc.HasValue)
        {
            var start = startDateUtc.Value.Date;
            query = query.Where(x => x.CreatedUtc >= start);
        }

        if (endDateUtc.HasValue)
        {
            var endExclusive = endDateUtc.Value.Date.AddDays(1);
            query = query.Where(x => x.CreatedUtc < endExclusive);
        }

        var records = await query
            .OrderByDescending(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);

        return records
            .Select(record => EntityCoreStoreSerializer.Deserialize<AICompletionUsageRecord>(record.Document.Content))
            .ToList();
    }
}
