using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.EntityCore.Models;
using Microsoft.EntityFrameworkCore;

namespace CrestApps.Core.Data.EntityCore.Services;

/// <summary>
/// EntityCore implementation of <see cref="IAIChatSessionStore"/> providing unscoped
/// access to AI chat sessions for background processing and administrative operations.
/// </summary>
public sealed class EntityCoreAIChatSessionStore : IAIChatSessionStore
{
    private readonly CrestAppsEntityDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreAIChatSessionStore"/> class.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    public EntityCoreAIChatSessionStore(CrestAppsEntityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Finds a chat session by its session identifier without user-scoping.
    /// </summary>
    /// <param name="sessionId">The unique session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching session, or <see langword="null"/> if not found.</returns>
    public async Task<AIChatSession> FindByIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var record = await _dbContext.AIChatSessionRecords
            .AsNoTracking()
            .Include(x => x.Document)
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);

        return record is null ? null : Materialize(record);
    }

    /// <summary>
    /// Retrieves all active sessions for the specified profile that have been inactive
    /// since before the given cutoff time.
    /// </summary>
    /// <param name="profileId">The profile identifier.</param>
    /// <param name="cutoffUtc">The UTC cutoff time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A read-only list of inactive active sessions.</returns>
    public async Task<IReadOnlyList<AIChatSession>> GetInactiveActiveSessionsAsync(
        string profileId,
        DateTime cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileId);

        var records = await _dbContext.AIChatSessionRecords
            .AsNoTracking()
            .Include(x => x.Document)
            .Where(x => x.ProfileId == profileId
                && x.Status == ChatSessionStatus.Active
                && x.LastActivityUtc < cutoffUtc)
            .ToListAsync(cancellationToken);

        return records.Select(Materialize).ToList();
    }

    /// <summary>
    /// Retrieves all closed or abandoned sessions for the specified profile.
    /// </summary>
    /// <param name="profileId">The profile identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A read-only list of closed or abandoned sessions.</returns>
    public async Task<IReadOnlyList<AIChatSession>> GetClosedSessionsAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileId);

        var records = await _dbContext.AIChatSessionRecords
            .AsNoTracking()
            .Include(x => x.Document)
            .Where(x => x.ProfileId == profileId
                && (x.Status == ChatSessionStatus.Closed || x.Status == ChatSessionStatus.Abandoned))
            .ToListAsync(cancellationToken);

        return records.Select(Materialize).ToList();
    }

    /// <summary>
    /// Retrieves a profile's sessions by their last activity, oldest first.
    /// </summary>
    /// <param name="profileId">The profile identifier.</param>
    /// <param name="afterUtc">When set, only sessions whose last activity is later than this are returned.</param>
    /// <param name="beforeUtc">Only sessions whose last activity is earlier than this are returned.</param>
    /// <param name="count">The largest number of sessions to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sessions, ordered by their last activity.</returns>
    public async Task<IReadOnlyList<AIChatSession>> ListByLastActivityAsync(
        string profileId,
        DateTime? afterUtc,
        DateTime beforeUtc,
        int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var query = _dbContext.AIChatSessionRecords
            .AsNoTracking()
            .Include(x => x.Document)
            .Where(x => x.ProfileId == profileId && x.LastActivityUtc < beforeUtc);

        if (afterUtc.HasValue)
        {
            var after = afterUtc.Value;
            query = query.Where(x => x.LastActivityUtc > after);
        }

        var records = await query
            .OrderBy(x => x.LastActivityUtc)
            .Take(count)
            .ToListAsync(cancellationToken);

        return records.Select(Materialize).ToList();
    }

    /// <summary>
    /// Deletes a chat session and its prompts.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the chat session.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the session existed and was deleted; otherwise <see langword="false"/>.</returns>
    public async Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var record = await _dbContext.AIChatSessionRecords
            .Include(x => x.Document)
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);

        if (record is null)
        {
            return false;
        }

        var promptEntityType = CatalogRecordFactory.GetEntityType<AIChatSessionPrompt>();
        var promptRecords = await _dbContext.CatalogRecords
            .Include(x => x.Document)
            .Where(x => x.EntityType == promptEntityType && x.SessionId == sessionId)
            .ToListAsync(cancellationToken);

        if (promptRecords.Count > 0)
        {
            _dbContext.Documents.RemoveRange(promptRecords.Select(x => x.Document).Where(x => x is not null));
            _dbContext.CatalogRecords.RemoveRange(promptRecords);
        }

        _dbContext.AIChatSessionRecords.Remove(record);

        if (record.Document is not null)
        {
            _dbContext.Documents.Remove(record.Document);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// Persists the specified chat session.
    /// </summary>
    /// <param name="chatSession">The chat session to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SaveAsync(AIChatSession chatSession, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatSession);

        var record = await _dbContext.AIChatSessionRecords
            .Include(x => x.Document)
            .FirstOrDefaultAsync(x => x.SessionId == chatSession.SessionId, cancellationToken);

        if (record is null)
        {
            _dbContext.AIChatSessionRecords.Add(CreateRecord(chatSession));
        }
        else
        {
            UpdateRecord(record, chatSession);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static AIChatSession Materialize(AIChatSessionRecord record)
    {
        return EntityCoreStoreSerializer.Deserialize<AIChatSession>(record.Document.Content);
    }

    private static AIChatSessionRecord CreateRecord(AIChatSession session)
    {
        return new()
        {
            Document = new DocumentRecord
            {
                Type = typeof(AIChatSession).FullName!,
                Content = EntityCoreStoreSerializer.Serialize(session),
            },
            SessionId = session.SessionId,
            ProfileId = session.ProfileId,
            Title = session.Title,
            UserId = session.UserId,
            ClientId = session.ClientId,
            Status = session.Status,
            CreatedUtc = session.CreatedUtc,
            LastActivityUtc = session.LastActivityUtc,
        };
    }

    private static void UpdateRecord(AIChatSessionRecord record, AIChatSession session)
    {
        record.ProfileId = session.ProfileId;
        record.Title = session.Title;
        record.UserId = session.UserId;
        record.ClientId = session.ClientId;
        record.Status = session.Status;
        record.CreatedUtc = session.CreatedUtc;
        record.LastActivityUtc = session.LastActivityUtc;
        record.Document.Content = EntityCoreStoreSerializer.Serialize(session);
    }
}
