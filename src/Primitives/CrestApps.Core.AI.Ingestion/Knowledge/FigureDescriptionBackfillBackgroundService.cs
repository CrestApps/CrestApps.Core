using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Ingestion.Knowledge;

/// <summary>
/// A thin hosted job that periodically drives <see cref="IFigureDescriptionBackfillService.BackfillDueAsync"/>.
/// All of the transcription logic lives in the service, so a host that prefers its own scheduling (or a
/// manual trigger) can invoke the service directly without this background job.
/// </summary>
internal sealed class FigureDescriptionBackfillBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly KnowledgeIngestionOptions _options;
    private readonly ILogger<FigureDescriptionBackfillBackgroundService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FigureDescriptionBackfillBackgroundService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="options">The knowledge ingestion options.</param>
    /// <param name="logger">The logger.</param>
    public FigureDescriptionBackfillBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<KnowledgeIngestionOptions> options,
        ILogger<FigureDescriptionBackfillBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.BackfillIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var backfillService = scope.ServiceProvider.GetService<IFigureDescriptionBackfillService>();

                if (backfillService is null)
                {
                    continue;
                }

                await backfillService.BackfillDueAsync(stoppingToken);

                // The transcriptions were written to the knowledge store through the transactional session.
                // A request commits that through its filter; this scope has no filter, so the session is
                // flushed here or every description is lost and paid for again on the next pass.
                var committer = scope.ServiceProvider.GetService<IStoreCommitter>();

                if (committer is not null)
                {
                    await committer.CommitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while transcribing pending figures.");
            }
        }
    }
}
