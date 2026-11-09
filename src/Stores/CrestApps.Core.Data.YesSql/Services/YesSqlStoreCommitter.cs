using System.Diagnostics;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

/// <summary>
/// Commits all staged YesSql writes by flushing the current <see cref="ISession"/>.
/// Registered automatically by <c>AddYesSqlDataStore</c>.
/// </summary>
public sealed class YesSqlStoreCommitter : IStoreCommitter
{
    private readonly ISession _session;
    private readonly ILogger<YesSqlStoreCommitter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlStoreCommitter"/> class.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="logger">The logger.</param>
    public YesSqlStoreCommitter(
        ISession session,
        ILogger<YesSqlStoreCommitter> logger)
    {
        _session = session;
        _logger = logger;
    }

    /// <summary>
    /// Commits the operation.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("YesSqlStoreCommitter flushing the current YesSql session.");

        using var activity = CrestAppsTelemetry.ActivitySource.StartActivity("crestapps.store.commit");
        activity?.SetTag(CrestAppsTelemetry.StoreTag, "YesSql");

        var startTimestamp = Stopwatch.GetTimestamp();
        var outcome = CrestAppsTelemetry.OutcomeOk;

        try
        {
            await _session.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            outcome = CrestAppsTelemetry.GetOutcome(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message).AddException(ex);

            throw;
        }
        finally
        {
            CrestAppsTelemetry.StoreCommitDuration.Record(
                Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                new KeyValuePair<string, object>(CrestAppsTelemetry.StoreTag, "YesSql"),
                new KeyValuePair<string, object>(CrestAppsTelemetry.OutcomeTag, outcome));
        }
    }
}
