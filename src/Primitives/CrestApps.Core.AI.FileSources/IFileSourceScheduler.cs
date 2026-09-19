using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// What one pass over the due sources did.
/// </summary>
/// <param name="Considered">How many configured sources were looked at.</param>
/// <param name="Ran">How many were due and were run.</param>
/// <param name="Failed">How many threw while running. Their failure is logged, not rethrown.</param>
public readonly record struct FileSourceSchedulerResult(int Considered, int Ran, int Failed);

/// <summary>
/// Finds the ingestion sources that are due and runs them.
/// </summary>
/// <remarks>
/// This is the whole of the periodic work, kept apart from the hosted service that happens to drive it here.
/// A host with its own scheduling — Orchard Core's background tasks, a cron job, a queue worker, an operator
/// pressing a button — resolves this and calls it, and gets the same behaviour without taking
/// <c>FileSourceBackgroundService</c> or its timer with it.
/// <para>
/// It is registered as scoped and deliberately creates no scope of its own, so the caller's scope — a shell
/// scope, a request, a unit of work — is the one the reads and writes happen in.
/// </para>
/// </remarks>
public interface IFileSourceScheduler
{
    /// <summary>
    /// Runs every source that is due.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the pass did.</returns>
    /// <remarks>
    /// A source that throws is logged and skipped: one source that cannot run is one source, and the others
    /// still get their turn.
    /// </remarks>
    Task<FileSourceSchedulerResult> RunDueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the sources that are due to run.
    /// </summary>
    /// <param name="now">The time to measure against.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sources that are due, in no particular order.</returns>
    /// <remarks>
    /// Exposed separately so a host can spread the work over its own workers, or show an operator what is
    /// about to happen, without reimplementing what "due" means.
    /// </remarks>
    Task<IReadOnlyList<IngestionSource>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether one source is due, from the run summary stored on it.
    /// </summary>
    /// <param name="ingestionSource">The configured source.</param>
    /// <param name="now">The time to measure against.</param>
    /// <returns><see langword="true"/> when the source has never run or its interval has elapsed.</returns>
    bool IsDue(IngestionSource ingestionSource, DateTimeOffset now);
}
