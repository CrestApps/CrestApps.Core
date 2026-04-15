using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Blazor.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace CrestApps.Core.Blazor.Web.Areas.AIChat.Services;

public sealed class MvcAIChatSessionExtractedDataService : IAIChatSessionExtractedDataRecorder
{
    private readonly BlazorAppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public MvcAIChatSessionExtractedDataService(BlazorAppDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task RecordExtractedDataAsync(AIProfile profile, AIChatSession session, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.ExtractedDataRecords.FirstOrDefaultAsync(x => x.SessionId == session.SessionId, cancellationToken);
        if (session.ExtractedData.Count == 0)
        {
            if (existing is not null) { _dbContext.ExtractedDataRecords.Remove(existing); await _dbContext.SaveChangesAsync(cancellationToken); }
            return;
        }

        var record = existing ?? new AIChatSessionExtractedDataRecord { ItemId = Guid.NewGuid().ToString("N"), SessionId = session.SessionId };
        record.ProfileId = profile.ItemId;
        record.SessionStartedUtc = session.CreatedUtc;
        record.SessionEndedUtc = session.ClosedAtUtc;
        record.UpdatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        record.Values = session.ExtractedData.Where(pair => pair.Value.Values.Count > 0).ToDictionary(pair => pair.Key, pair => pair.Value.Values.ToList(), StringComparer.OrdinalIgnoreCase);

        if (existing is null) _dbContext.ExtractedDataRecords.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AIChatSessionExtractedDataRecord>> GetAsync(string profileId, DateTime? startDateUtc, DateTime? endDateUtc, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ExtractedDataRecords.Where(x => x.ProfileId == profileId);
        if (startDateUtc.HasValue) query = query.Where(x => x.SessionStartedUtc >= startDateUtc.Value.Date);
        if (endDateUtc.HasValue) query = query.Where(x => x.SessionStartedUtc < endDateUtc.Value.Date.AddDays(1));
        return await query.OrderByDescending(x => x.SessionStartedUtc).ToListAsync(cancellationToken);
    }
}
