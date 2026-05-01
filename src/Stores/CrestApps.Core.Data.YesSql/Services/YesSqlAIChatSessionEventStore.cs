using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;
using Microsoft.Extensions.Options;
using YesSql;

using ISession = YesSql.ISession;

namespace CrestApps.Core.Data.YesSql.Services;

/// <summary>
/// YesSql-backed store for chat-session analytics events.
/// </summary>
public sealed class YesSqlAIChatSessionEventStore : IAIChatSessionEventStore
{
    private readonly ISession _session;
    private readonly string _collectionName;

    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlAIChatSessionEventStore"/> class.
    /// </summary>
    /// <param name="session">The YesSql session.</param>
    /// <param name="options">The YesSql store options.</param>
    public YesSqlAIChatSessionEventStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
    {
        _session = session;
        _collectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Finds a chat-session analytics record by session identifier.
    /// </summary>
    /// <param name="sessionId">The chat session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<AIChatSessionEvent> FindBySessionIdAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return _session.Query<AIChatSessionEvent, AIChatSessionMetricsIndex>(x => x.SessionId == sessionId, collection: _collectionName)
            .FirstOrDefaultAsync(cancellationToken);
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

        await _session.SaveAsync(chatSessionEvent, false, _collectionName, cancellationToken);
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
        var query = _session.Query<AIChatSessionEvent, AIChatSessionMetricsIndex>(collection: _collectionName);

        if (!string.IsNullOrEmpty(profileId))
        {
            query = query.Where(x => x.ProfileId == profileId);
        }

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

        var events = await query.ListAsync(cancellationToken);

        return events.OrderByDescending(x => x.SessionStartedUtc).ToList();
    }
}
