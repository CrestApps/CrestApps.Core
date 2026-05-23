using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;
using Microsoft.Extensions.Options;
using YesSql;
using ISession = YesSql.ISession;

namespace CrestApps.Core.Data.YesSql.Services;

/// <summary>
/// YesSql implementation of <see cref="IAIChatSessionStore"/> providing unscoped
/// access to AI chat sessions for background processing and administrative operations.
/// </summary>
public sealed class YesSqlAIChatSessionStore : IAIChatSessionStore
{
    private readonly ISession _session;
    private readonly string _collection;

    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlAIChatSessionStore"/> class.
    /// </summary>
    /// <param name="session">The YesSql session.</param>
    /// <param name="options">The YesSql store options.</param>
    public YesSqlAIChatSessionStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
    {
        _session = session;
        _collection = options.Value.AICollectionName;
    }

    /// <summary>
    /// Finds a chat session by its unique session identifier.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the chat session.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching session, or <see langword="null"/> if not found.</returns>
    public async Task<AIChatSession> FindByIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        return await _session.Query<AIChatSession, AIChatSessionIndex>(
            x => x.SessionId == sessionId,
            collection: _collection)
            .FirstOrDefaultAsync(cancellationToken);
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

        var sessions = await _session.Query<AIChatSession, AIChatSessionIndex>(
            i => i.ProfileId == profileId && i.Status == ChatSessionStatus.Active && i.LastActivityUtc < cutoffUtc,
            collection: _collection)
            .ListAsync(cancellationToken);

        return sessions.ToList();
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

        var sessions = await _session.Query<AIChatSession, AIChatSessionIndex>(
            i => i.ProfileId == profileId
                && (i.Status == ChatSessionStatus.Closed || i.Status == ChatSessionStatus.Abandoned),
            collection: _collection)
            .ListAsync(cancellationToken);

        return sessions.ToList();
    }

    /// <summary>
    /// Persists the specified chat session.
    /// </summary>
    /// <param name="chatSession">The chat session to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SaveAsync(AIChatSession chatSession, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatSession);

        var storedSession = await FindByIdAsync(chatSession.SessionId, cancellationToken);

        if (storedSession == null)
        {
            await _session.SaveAsync(chatSession, _collection);

            return;
        }

        if (!ReferenceEquals(storedSession, chatSession))
        {
            CopySession(chatSession, storedSession);
        }

        await _session.SaveAsync(storedSession, _collection);
    }

    private static void CopySession(AIChatSession source, AIChatSession destination)
    {
        destination.SessionId = source.SessionId;
        destination.ProfileId = source.ProfileId;
        destination.Title = source.Title;
        destination.UserId = source.UserId;
        destination.ClientId = source.ClientId;
        destination.Documents = source.Documents == null
            ? []
            : [.. source.Documents];
        destination.CreatedUtc = source.CreatedUtc;
        destination.ModifiedUtc = source.ModifiedUtc;
        destination.LastActivityUtc = source.LastActivityUtc;
        destination.ClosedAtUtc = source.ClosedAtUtc;
        destination.Status = source.Status;
        destination.ResponseHandlerName = source.ResponseHandlerName;
        destination.ExtractedData = source.ExtractedData == null
            ? []
            : new Dictionary<string, ExtractedFieldState>(source.ExtractedData);
        destination.PostSessionResults = source.PostSessionResults == null
            ? []
            : new Dictionary<string, PostSessionResult>(source.PostSessionResults);
        destination.PostSessionProcessingStatus = source.PostSessionProcessingStatus;
        destination.PostSessionProcessingAttempts = source.PostSessionProcessingAttempts;
        destination.PostSessionProcessingLastAttemptUtc = source.PostSessionProcessingLastAttemptUtc;
        destination.IsPostSessionTasksProcessed = source.IsPostSessionTasksProcessed;
        destination.IsAnalyticsRecorded = source.IsAnalyticsRecorded;
        destination.IsConversionGoalsEvaluated = source.IsConversionGoalsEvaluated;
        destination.Properties = source.Properties == null
            ? []
            : new Dictionary<string, object>(source.Properties);
    }
}
