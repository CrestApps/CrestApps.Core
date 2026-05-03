using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.EntityCore.Models;
using Microsoft.EntityFrameworkCore;

namespace CrestApps.Core.Data.EntityCore.Services;

/// <summary>
/// Entity Framework Core-backed store for chat-session analytics events.
/// </summary>
public sealed class EntityCoreAIChatSessionEventStore : IAIChatSessionEventStore
{
    private readonly CrestAppsEntityDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreAIChatSessionEventStore"/> class.
    /// </summary>
    /// <param name="dbContext">The Entity Framework Core database context.</param>
    public EntityCoreAIChatSessionEventStore(CrestAppsEntityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Finds a chat-session analytics record by session identifier.
    /// </summary>
    /// <param name="sessionId">The chat session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<AIChatSessionEvent> FindBySessionIdAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var record = await _dbContext.AIChatSessionEventRecords
            .AsNoTracking()
            .Include(x => x.Document)
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);

        return record is null ? null : Materialize(record);
    }

    /// <summary>
    /// Saves a chat-session analytics record.
    /// </summary>
    /// <param name="chatSessionEvent">The analytics record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SaveAsync(
        AIChatSessionEvent chatSessionEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatSessionEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(chatSessionEvent.SessionId);

        var existing = await _dbContext.AIChatSessionEventRecords
            .Include(x => x.Document)
            .FirstOrDefaultAsync(x => x.SessionId == chatSessionEvent.SessionId, cancellationToken);

        if (existing is null)
        {
            _dbContext.AIChatSessionEventRecords.Add(CreateRecord(chatSessionEvent));

            return;
        }

        UpdateRecord(existing, chatSessionEvent);
    }

    /// <summary>
    /// Retrieves chat-session analytics records matching the optional profile and date filters.
    /// </summary>
    /// <param name="profileId">The optional profile identifier filter.</param>
    /// <param name="startDateUtc">The inclusive UTC start date filter.</param>
    /// <param name="endDateUtc">The inclusive UTC end date filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<IReadOnlyList<AIChatSessionEvent>> GetAsync(
        string profileId,
        DateTime? startDateUtc,
        DateTime? endDateUtc,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.AIChatSessionEventRecords
            .AsNoTracking()
            .Include(x => x.Document);

        IQueryable<AIChatSessionEventStoreRecord> filtered = query;

        if (!string.IsNullOrEmpty(profileId))
        {
            filtered = filtered.Where(x => x.ProfileId == profileId);
        }

        if (startDateUtc.HasValue)
        {
            var start = startDateUtc.Value.Date;
            filtered = filtered.Where(x => x.SessionStartedUtc >= start);
        }

        if (endDateUtc.HasValue)
        {
            var endExclusive = endDateUtc.Value.Date.AddDays(1);
            filtered = filtered.Where(x => x.SessionStartedUtc < endExclusive);
        }

        var records = await filtered
            .OrderByDescending(x => x.SessionStartedUtc)
            .ToListAsync(cancellationToken);

        return records.Select(Materialize).ToList();
    }

    private static AIChatSessionEventStoreRecord CreateRecord(AIChatSessionEvent chatSessionEvent)
    {
        return new()
        {
            Document = new DocumentRecord
            {
                Type = typeof(AIChatSessionEvent).FullName!,
                Content = EntityCoreStoreSerializer.Serialize(chatSessionEvent),
            },
            SessionId = chatSessionEvent.SessionId,
            ProfileId = chatSessionEvent.ProfileId,
            SessionStartedUtc = chatSessionEvent.SessionStartedUtc,
            CreatedUtc = chatSessionEvent.CreatedUtc,
        };
    }

    private static AIChatSessionEvent Materialize(AIChatSessionEventStoreRecord record)
    {
        return EntityCoreStoreSerializer.Deserialize<AIChatSessionEvent>(record.Document.Content);
    }

    private static void UpdateRecord(AIChatSessionEventStoreRecord destination, AIChatSessionEvent source)
    {
        destination.ProfileId = source.ProfileId;
        destination.SessionStartedUtc = source.SessionStartedUtc;
        destination.CreatedUtc = source.CreatedUtc;
        destination.Document.Content = EntityCoreStoreSerializer.Serialize(source);
    }
}
