using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;
using Microsoft.Extensions.Options;
using YesSql;
using ISession = YesSql.ISession;

namespace CrestApps.Core.Mvc.Web.Areas.AIChat.Services;

public sealed class SampleAIChatSessionExtractedDataService : IAIChatSessionExtractedDataRecorder
{
    private readonly ISession _session;
    private readonly YesSqlStoreOptions _options;
    private readonly TimeProvider _timeProvider;

    public SampleAIChatSessionExtractedDataService(
        ISession session,
        IOptions<YesSqlStoreOptions> options,
        TimeProvider timeProvider)
    {
        _session = session;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task RecordExtractedDataAsync(
        AIProfile profile,
        AIChatSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(session);

        var existing = await FindBySessionIdAsync(session.SessionId);
        if (session.ExtractedData.Count == 0)
        {
            if (existing is not null)
            {
                _session.Delete(existing);
            }

            return;
        }

        var record = existing ?? new AIChatSessionExtractedDataRecord
        {
            ItemId = session.SessionId,
            SessionId = session.SessionId,
        };
        record.ProfileId = profile.ItemId;
        record.SessionStartedUtc = session.CreatedUtc;
        record.SessionEndedUtc = session.ClosedAtUtc;
        record.UpdatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        record.Values = session.ExtractedData.Where(pair => pair.Value.Values.Count > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Values.ToList(), StringComparer.OrdinalIgnoreCase);

        await _session.SaveAsync(record);
    }

    public async Task<IReadOnlyList<AIChatSessionExtractedDataRecord>> GetAsync(string profileId, DateTime? startDateUtc, DateTime? endDateUtc, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var query = _session.Query<AIChatSessionExtractedDataRecord, AIChatSessionExtractedDataIndex>(x => x.ProfileId == profileId, collection: _options.AICollectionName);
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

        return records.OrderByDescending(x => x.SessionStartedUtc).ToList();
    }

    private async Task<AIChatSessionExtractedDataRecord> FindBySessionIdAsync(string sessionId)
    {
        return await _session.Query<AIChatSessionExtractedDataRecord, AIChatSessionExtractedDataIndex>(x => x.SessionId == sessionId, collection: _options.AICollectionName)
            .FirstOrDefaultAsync();
    }
}
