using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// The default <see cref="IFileSourceScheduler"/>.
/// </summary>
/// <remarks>
/// Every <see cref="FileSource"/> is a candidate, because reading files into a knowledge base is the only
/// thing a file source does. It reads one store and knows about one kind of record: web crawlers are the
/// web crawlers feature's to schedule, including the ones it reads through the same ingestion pipeline.
/// </remarks>
public sealed class DefaultFileSourceScheduler : IFileSourceScheduler
{
    private readonly IFileSourceStore _fileSourceStore;
    private readonly IAIDataSourceStore _dataSourceStore;
    private readonly IIngestionConnectorResolver _connectorResolver;
    private readonly IIngestionRunService _runService;
    private readonly IStoreCommitter _committer;
    private readonly FileSourceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DefaultFileSourceScheduler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultFileSourceScheduler"/> class.
    /// </summary>
    /// <param name="fileSourceStore">The file source store.</param>
    /// <param name="dataSourceStore">The data source store.</param>
    /// <param name="connectorResolver">The connector resolver.</param>
    /// <param name="runService">The run service.</param>
    /// <param name="options">The file source options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="committer">
    /// The store committer, when the host has one. A request commits through its own filter; a background
    /// pass has no filter, so what a run wrote is flushed here or it is lost.
    /// </param>
    public DefaultFileSourceScheduler(
        IFileSourceStore fileSourceStore,
        IAIDataSourceStore dataSourceStore,
        IIngestionConnectorResolver connectorResolver,
        IIngestionRunService runService,
        IOptions<FileSourceOptions> options,
        TimeProvider timeProvider,
        ILogger<DefaultFileSourceScheduler> logger,
        IStoreCommitter committer = null)
    {
        _fileSourceStore = fileSourceStore;
        _dataSourceStore = dataSourceStore;
        _connectorResolver = connectorResolver;
        _runService = runService;
        _committer = committer;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FileSourceSchedulerResult> RunDueAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var candidates = await GetCandidatesAsync(cancellationToken);
        var ran = 0;
        var failed = 0;

        foreach (var ingestionSource in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsDue(ingestionSource, now))
            {
                continue;
            }

            try
            {
                var summary = await _runService.RunAsync(ingestionSource, cancellationToken);

                ran++;

                if (_committer is not null)
                {
                    await _committer.CommitAsync(cancellationToken);
                }

                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Source '{SourceId}' saw {Discovered} item(s), ingested {Ingested}, removed {Removed}, failed {Failed}. Listing complete: {Complete}.",
                        ingestionSource.ItemId,
                        summary.ItemsDiscovered,
                        summary.ItemsIndexed,
                        summary.ItemsDeleted,
                        summary.ItemsFailed,
                        summary.DiscoveryCompleted);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One source that cannot run is one source. The others still get their turn.
                failed++;
                _logger.LogError(ex, "Source '{SourceId}' failed to run.", ingestionSource.ItemId);
            }
        }

        return new FileSourceSchedulerResult(candidates.Count, ran, failed);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IngestionSource>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var candidates = await GetCandidatesAsync(cancellationToken);

        return candidates.Where(candidate => IsDue(candidate, now)).ToArray();
    }

    /// <inheritdoc />
    public bool IsDue(IngestionSource ingestionSource, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ingestionSource);

        var every = TimeSpan.FromMinutes(Math.Max(1, ingestionSource.ReindexIntervalMinutes ?? _options.DefaultRunIntervalMinutes));

        if (!ingestionSource.TryGet<FileSourceRunSummary>(out var last) || last.StartedUtc == default)
        {
            return true;
        }

        return now - new DateTimeOffset(last.StartedUtc, TimeSpan.Zero) >= every;
    }

    /// <summary>
    /// Collects everything this scheduler is responsible for, whether or not it is due yet.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sources that could be run.</returns>
    private async Task<IReadOnlyList<IngestionSource>> GetCandidatesAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<IngestionSource>();

        foreach (var fileSource in await _fileSourceStore.GetAllAsync(cancellationToken))
        {
            if (IsRunnable(fileSource))
            {
                candidates.Add(fileSource);
            }
        }

        return candidates;
    }

    /// <summary>
    /// Determines whether a record could be run at all, before asking whether it is due.
    /// </summary>
    /// <param name="ingestionSource">The configured source.</param>
    /// <returns><see langword="true"/> when it is enabled, has a target, and has a connector to read it.</returns>
    private bool IsRunnable(IngestionSource ingestionSource)
    {
        if (!ingestionSource.Enabled || string.IsNullOrWhiteSpace(ingestionSource.AIDataSourceId))
        {
            return false;
        }

        // Whatever registered the connector may be gone. The record is still stored, but nothing can read it.
        return _connectorResolver.Get(ingestionSource.Source) is not null;
    }
}
