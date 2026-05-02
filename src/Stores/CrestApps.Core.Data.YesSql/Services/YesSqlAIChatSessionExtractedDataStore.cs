using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;
using Microsoft.Extensions.Options;
using YesSql;

using ISession = YesSql.ISession;

namespace CrestApps.Core.Data.YesSql.Services;

/// <summary>
/// YesSql-backed extracted-data snapshot store for AI chat sessions.
/// </summary>
public sealed class YesSqlAIChatSessionExtractedDataStore : IAIChatSessionExtractedDataStore
{
    private readonly ISession _session;
    private readonly string _collection;

    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlAIChatSessionExtractedDataStore"/> class.
    /// </summary>
    /// <param name="session">The YesSql session.</param>
    /// <param name="options">The store options.</param>
    public YesSqlAIChatSessionExtractedDataStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
    {
        _session = session;
        _collection = options.Value.AICollectionName;
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

        var existing = await _session.Query<AIChatSessionExtractedDataRecord, AIChatSessionExtractedDataIndex>(
                x => x.SessionId == record.SessionId,
                collection: _collection)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            await _session.SaveAsync(record, _collection);

            return;
        }

        if (!ReferenceEquals(existing, record))
        {
            existing.ItemId = record.ItemId;
            existing.SessionId = record.SessionId;
            existing.ProfileId = record.ProfileId;
            existing.SessionStartedUtc = record.SessionStartedUtc;
            existing.SessionEndedUtc = record.SessionEndedUtc;
            existing.UpdatedUtc = record.UpdatedUtc;
            existing.Values = record.Values == null
                ? []
                : record.Values.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value?.ToList() ?? [],
                    StringComparer.OrdinalIgnoreCase);
        }

        await _session.SaveAsync(existing, _collection);
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

        var existing = await _session.Query<AIChatSessionExtractedDataRecord, AIChatSessionExtractedDataIndex>(
                x => x.SessionId == sessionId,
                collection: _collection)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            return false;
        }

        _session.Delete(existing, _collection);

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

        var query = _session.Query<AIChatSessionExtractedDataRecord, AIChatSessionExtractedDataIndex>(
            x => x.ProfileId == profileId,
            collection: _collection);

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

        var records = await query.ListAsync(cancellationToken);

        return records
            .OrderByDescending(x => x.SessionStartedUtc)
            .ToList();
    }
}
