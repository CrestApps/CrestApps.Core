using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// Runs one ingestion source: list what it holds, ingest what is new or changed, and remove what is gone.
/// </summary>
/// <remarks>
/// It takes an <see cref="IngestionSource"/> rather than one kind of record, because what it does is the
/// same for a <see cref="FileSource"/> reading a folder and for a <see cref="WebCrawler"/> whose strategy
/// feeds an ingested data source. Which store the record came from is not its concern.
/// </remarks>
public interface IFileSourceRunService
{
    /// <summary>
    /// Runs one ingestion source.
    /// </summary>
    /// <param name="ingestionSource">The configured source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the run did.</returns>
    Task<FileSourceRunSummary> RunAsync(IngestionSource ingestionSource, CancellationToken cancellationToken = default);
}
