using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.AIChat;
using Microsoft.Extensions.Options;
using YesSql;

using ISession = YesSql.ISession;

namespace CrestApps.Core.Data.YesSql.Services;

/// <summary>
/// YesSql-backed store for AI completion usage records.
/// </summary>
public sealed class YesSqlAICompletionUsageStore : IAICompletionUsageStore
{
    private readonly ISession _session;
    private readonly string _collectionName;

    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlAICompletionUsageStore"/> class.
    /// </summary>
    /// <param name="session">The YesSql session.</param>
    /// <param name="options">The YesSql store options.</param>
    public YesSqlAICompletionUsageStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
    {
        _session = session;
        _collectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Saves a usage record.
    /// </summary>
    /// <param name="record">The usage record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task SaveAsync(
        AICompletionUsageRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _session.SaveAsync(record, false, _collectionName, cancellationToken);
    }

    /// <summary>
    /// Retrieves usage records captured within the optional UTC date range.
    /// </summary>
    /// <param name="startDateUtc">The inclusive UTC start date filter.</param>
    /// <param name="endDateUtc">The inclusive UTC end date filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<IReadOnlyList<AICompletionUsageRecord>> GetAsync(
        DateTime? startDateUtc,
        DateTime? endDateUtc,
        CancellationToken cancellationToken = default)
    {
        var query = _session.Query<AICompletionUsageRecord, AICompletionUsageIndex>(collection: _collectionName);

        if (startDateUtc.HasValue)
        {
            var start = startDateUtc.Value.Date;
            query = query.Where(x => x.CreatedUtc >= start);
        }

        if (endDateUtc.HasValue)
        {
            var endExclusive = endDateUtc.Value.Date.AddDays(1);
            query = query.Where(x => x.CreatedUtc < endExclusive);
        }

        var records = await query.ListAsync(cancellationToken);

        return records.OrderByDescending(x => x.CreatedUtc).ToList();
    }
}
