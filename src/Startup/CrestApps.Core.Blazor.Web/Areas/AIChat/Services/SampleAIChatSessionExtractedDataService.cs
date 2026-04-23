using System.Collections.Concurrent;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Blazor.Web.Areas.AIChat.Services;

public sealed class SampleAIChatSessionExtractedDataService : IAIChatSessionExtractedDataRecorder
{
    private static readonly ConcurrentDictionary<string, AIChatSessionExtractedDataRecord> _store = new(StringComparer.OrdinalIgnoreCase);

    public Task RecordExtractedDataAsync(AIProfile profile, AIChatSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(session);

        if (session.ExtractedData.Count == 0)
        {
            _store.TryRemove(session.SessionId, out _);

            return Task.CompletedTask;
        }

        var record = _store.GetOrAdd(session.SessionId, _ => new AIChatSessionExtractedDataRecord
        {
            ItemId = session.SessionId,
            SessionId = session.SessionId,
        });

        record.ProfileId = profile.ItemId;
        record.SessionStartedUtc = session.CreatedUtc;
        record.SessionEndedUtc = session.ClosedAtUtc;
        record.UpdatedUtc = TimeProvider.System.GetUtcNow().UtcDateTime;
        record.Values = session.ExtractedData
            .Where(pair => pair.Value.Values.Count > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Values.ToList(), StringComparer.OrdinalIgnoreCase);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AIChatSessionExtractedDataRecord>> GetAsync(string profileId, DateTime? startDateUtc, DateTime? endDateUtc, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var values = _store.Values
            .Where(x => string.Equals(x.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));

        if (startDateUtc.HasValue)
        {
            var start = startDateUtc.Value.Date;
            values = values.Where(x => x.SessionStartedUtc >= start);
        }

        if (endDateUtc.HasValue)
        {
            var endExclusive = endDateUtc.Value.Date.AddDays(1);
            values = values.Where(x => x.SessionStartedUtc < endExclusive);
        }

        IReadOnlyList<AIChatSessionExtractedDataRecord> result = values
            .OrderByDescending(x => x.SessionStartedUtc)
            .ToList();

        return Task.FromResult(result);
    }
}
