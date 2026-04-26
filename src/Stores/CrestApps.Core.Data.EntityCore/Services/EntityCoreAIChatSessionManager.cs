using System.Security.Claims;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.ResponseHandling;
using CrestApps.Core.Data.EntityCore.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Data.EntityCore.Services;

public sealed class EntityCoreAIChatSessionManager : IAIChatSessionManager
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly CrestAppsEntityDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreAIChatSessionManager"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The http context accessor.</param>
    /// <param name="dbContext">The db context.</param>
    /// <param name="timeProvider">The time provider.</param>
    public EntityCoreAIChatSessionManager(
        IHttpContextAccessor httpContextAccessor,
        CrestAppsEntityDbContext dbContext,
        TimeProvider timeProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Finds by id.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<AIChatSession> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        var record = await _dbContext.AIChatSessionRecords.AsNoTracking().FirstOrDefaultAsync(x => x.SessionId == id, cancellationToken);
        return record is null ? null : Materialize(record);
    }

    /// <summary>
    /// Finds the operation.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<AIChatSession> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        return FindByIdAsync(id, cancellationToken);
    }

    /// <summary>
    /// Pages the operation.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<AIChatSessionResult> PageAsync(int page, int pageSize, AIChatSessionQueryContext context = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.AIChatSessionRecords.AsNoTracking();
        if (!string.IsNullOrEmpty(context?.ProfileId))
        {
            query = query.Where(x => x.ProfileId == context.ProfileId);
        }

        var skip = (page - 1) * pageSize;
        var total = await query.CountAsync(cancellationToken);
        var records = await query.OrderByDescending(x => x.CreatedUtc).ThenByDescending(x => x.LastActivityUtc).Skip(skip).Take(pageSize).ToListAsync(cancellationToken);

        return new AIChatSessionResult
        {
            Count = total,
            Sessions = records.Select(s => new AIChatSessionEntry { SessionId = s.SessionId, ProfileId = s.ProfileId, Title = s.Title, UserId = s.UserId, ClientId = s.ClientId, Status = s.Status, CreatedUtc = s.CreatedUtc, LastActivityUtc = s.LastActivityUtc, }),
        };
    }

    /// <summary>
    /// News the operation.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<AIChatSession> NewAsync(AIProfile profile, NewAIChatSessionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var session = new AIChatSession
        {
            SessionId = UniqueId.GenerateId(),
            ProfileId = profile.ItemId,
            CreatedUtc = now,
            LastActivityUtc = now,
            Status = ChatSessionStatus.Active,
        };
        var user = _httpContextAccessor.HttpContext?.User;
        var userId = user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? user?.Identity?.Name;

        if (!string.IsNullOrEmpty(userId))
        {
            session.UserId = userId;
        }

        if (profile.Type == AIProfileType.Chat)
        {
            if (profile.TryGet<AIProfileMetadata>(out var profileMetadata) && !string.IsNullOrWhiteSpace(profileMetadata.InitialPrompt))
            {
                // Stage the initial prompt directly in the change tracker so that SaveAsync
                // commits both the session and this prompt atomically in a single transaction.
                // If SaveAsync is never called, the scope disposes without committing and no
                // orphaned prompt is persisted.
                var prompt = new AIChatSessionPrompt
                {
                    ItemId = UniqueId.GenerateId(),
                    SessionId = session.SessionId,
                    Role = ChatRole.Assistant,
                    Title = profile.PromptSubject,
                    Content = profileMetadata.InitialPrompt,
                    CreatedUtc = now,
                };

                _dbContext.CatalogRecords.Add(CatalogRecordFactory.Create(prompt));
            }

            var handlerSettings = profile.GetSettings<ResponseHandlerProfileSettings>();

            if (!string.IsNullOrEmpty(handlerSettings.InitialResponseHandlerName))
            {
                session.ResponseHandlerName = handlerSettings.InitialResponseHandlerName;
            }
        }

        return Task.FromResult(session);
    }

    /// <summary>
    /// Saves the operation.
    /// </summary>
    /// <param name="chatSession">The chat session.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SaveAsync(AIChatSession chatSession, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatSession);

        chatSession.LastActivityUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var record = await _dbContext.AIChatSessionRecords.FirstOrDefaultAsync(x => x.SessionId == chatSession.SessionId, cancellationToken);

        if (record is null)
        {
            _dbContext.AIChatSessionRecords.Add(CreateRecord(chatSession));
        }
        else
        {
            UpdateRecord(record, chatSession);
        }
    }

    /// <summary>
    /// Deletes the operation.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var record = await _dbContext.AIChatSessionRecords.FirstOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);

        if (record is null)
        {
            return false;
        }

        var promptEntityType = CatalogRecordFactory.GetEntityType<AIChatSessionPrompt>();
        var promptRecords = await _dbContext.CatalogRecords
            .Where(x => x.EntityType == promptEntityType && x.SessionId == sessionId)
            .ToListAsync(cancellationToken);

        if (promptRecords.Count > 0)
        {
            _dbContext.CatalogRecords.RemoveRange(promptRecords);
        }

        _dbContext.AIChatSessionRecords.Remove(record);

        return true;
    }

    /// <summary>
    /// Deletes all.
    /// </summary>
    /// <param name="profileId">The profile id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<int> DeleteAllAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileId);

        var records = await _dbContext.AIChatSessionRecords.Where(x => x.ProfileId == profileId).ToListAsync(cancellationToken);

        if (records.Count == 0)
        {
            return 0;
        }

        var sessionIds = records.Select(x => x.SessionId).ToList();
        var promptEntityType = CatalogRecordFactory.GetEntityType<AIChatSessionPrompt>();
        var promptRecords = await _dbContext.CatalogRecords
            .Where(x => x.EntityType == promptEntityType && sessionIds.Contains(x.SessionId))
            .ToListAsync(cancellationToken);

        if (promptRecords.Count > 0)
        {
            _dbContext.CatalogRecords.RemoveRange(promptRecords);
        }

        _dbContext.AIChatSessionRecords.RemoveRange(records);

        return records.Count;
    }

    private static AIChatSession Materialize(AIChatSessionRecord record)
    {
        return EntityCoreStoreSerializer.Deserialize<AIChatSession>(record.Payload);
    }

    private static AIChatSessionRecord CreateRecord(AIChatSession session)
    {
        return new()
        {
            SessionId = session.SessionId,
            ProfileId = session.ProfileId,
            Title = session.Title,
            UserId = session.UserId,
            ClientId = session.ClientId,
            Status = session.Status,
            CreatedUtc = session.CreatedUtc,
            LastActivityUtc = session.LastActivityUtc,
            Payload = EntityCoreStoreSerializer.Serialize(session),
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
        record.Payload = EntityCoreStoreSerializer.Serialize(session);
    }
}
