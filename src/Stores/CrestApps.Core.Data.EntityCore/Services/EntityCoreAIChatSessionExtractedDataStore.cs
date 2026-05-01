using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.EntityCore.Models;
using Microsoft.EntityFrameworkCore;

namespace CrestApps.Core.Data.EntityCore.Services;

/// <summary>
/// Entity Framework Core-backed extracted-data snapshot store for AI chat sessions.
/// </summary>
public sealed class EntityCoreAIChatSessionExtractedDataStore : IAIChatSessionExtractedDataStore
{
    private readonly CrestAppsEntityDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreAIChatSessionExtractedDataStore"/> class.
    /// </summary>
    /// <param name="dbContext">The Entity Framework Core database context.</param>
    public EntityCoreAIChatSessionExtractedDataStore(CrestAppsEntityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Saves the extracted-data snapshot record.
    /// </summary>
    /// <param name="record">The record to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SaveAsync(
        AIChatSessionExtractedDataRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.SessionId);

        var existing = await _dbContext.AIChatSessionExtractedDataRecords
            .FirstOrDefaultAsync(x => x.SessionId == record.SessionId, cancellationToken);

        if (existing is null)
        {
            _dbContext.AIChatSessionExtractedDataRecords.Add(CreateRecord(record));

            return;
        }

        UpdateRecord(existing, record);
    }

    /// <summary>
    /// Deletes the extracted-data snapshot record for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when a record was deleted; otherwise <see langword="false"/>.</returns>
    public async Task<bool> DeleteAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var existing = await _dbContext.AIChatSessionExtractedDataRecords
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);

        if (existing is null)
        {
            return false;
        }

        _dbContext.AIChatSessionExtractedDataRecords.Remove(existing);

        return true;
    }

    /// <summary>
    /// Retrieves extracted-data snapshot records for the specified AI profile.
    /// </summary>
    /// <param name="profileId">The AI profile identifier.</param>
    /// <param name="startDateUtc">The inclusive UTC start date filter.</param>
    /// <param name="endDateUtc">The inclusive UTC end date filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<IReadOnlyList<AIChatSessionExtractedDataRecord>> GetAsync(
        string profileId,
        DateTime? startDateUtc,
        DateTime? endDateUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var query = _dbContext.AIChatSessionExtractedDataRecords
            .AsNoTracking()
            .Where(x => x.ProfileId == profileId);

        if (startDateUtc.HasValue)
        {
            var start = startDateUtc.Value.Date;
            query = query.Where(x => x.SessionStartedUtc >= start);
        }

        if (endDateUtc.HasValue)
        {
            var endExclusive = endDateUtc.Value.Date.AddDays(1);
            query = query.Where(x => x.SessionStartedUtc < endExclusive);
        }

        var records = await query
            .OrderByDescending(x => x.SessionStartedUtc)
            .ToListAsync(cancellationToken);

        return records
            .Select(Materialize)
            .ToList();
    }

    private static AIChatSessionExtractedDataStoreRecord CreateRecord(AIChatSessionExtractedDataRecord record)
    {
        return new()
        {
            SessionId = record.SessionId,
            ProfileId = record.ProfileId,
            SessionStartedUtc = record.SessionStartedUtc,
            SessionEndedUtc = record.SessionEndedUtc,
            UpdatedUtc = record.UpdatedUtc,
            Payload = EntityCoreStoreSerializer.Serialize(record),
        };
    }

    private static AIChatSessionExtractedDataRecord Materialize(AIChatSessionExtractedDataStoreRecord record)
    {
        return EntityCoreStoreSerializer.Deserialize<AIChatSessionExtractedDataRecord>(record.Payload);
    }

    private static void UpdateRecord(AIChatSessionExtractedDataStoreRecord destination, AIChatSessionExtractedDataRecord source)
    {
        destination.ProfileId = source.ProfileId;
        destination.SessionStartedUtc = source.SessionStartedUtc;
        destination.SessionEndedUtc = source.SessionEndedUtc;
        destination.UpdatedUtc = source.UpdatedUtc;
        destination.Payload = EntityCoreStoreSerializer.Serialize(source);
    }
}
