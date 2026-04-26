using CrestApps.Core.AI.DataSources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

internal sealed class AIDataSourceAlignmentBackgroundService : BackgroundService
{
    private static readonly TimeSpan _alignmentCheckInterval = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AIDataSourceAlignmentBackgroundService> _logger;

    private DateOnly? _lastRunDateUtc;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIDataSourceAlignmentBackgroundService"/> class.
    /// </summary>
    /// <param name="scopeFactory">The scope factory.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public AIDataSourceAlignmentBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<AIDataSourceAlignmentBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Executes the operation.
    /// </summary>
    /// <param name="stoppingToken">The cancellation token.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_alignmentCheckInterval);

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

            if (!ShouldRunAlignment(out var runDateUtc))
            {
                _logger.LogTrace("Skipping AI data-source alignment on this timer tick because the UTC schedule window has not been reached.");
                continue;
            }

            try
            {
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("Starting scheduled AI data-source alignment for UTC date {RunDateUtc}.", runDateUtc);
                }

                await using var scope = _scopeFactory.CreateAsyncScope();
                await AlignDataSourcesAsync(scope.ServiceProvider, stoppingToken);
                _lastRunDateUtc = runDateUtc;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during nightly AI data-source alignment.");
            }
        }
    }

    private bool ShouldRunAlignment(out DateOnly runDateUtc)
    {
        var utcNow = _timeProvider.GetUtcNow();
        runDateUtc = DateOnly.FromDateTime(utcNow.UtcDateTime);

return utcNow.Hour == 2 &&
            utcNow.Minute < 30 &&
            _lastRunDateUtc != runDateUtc;
    }

    private async Task AlignDataSourcesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var dataSourceStore = services.GetService<IAIDataSourceStore>();
        if (dataSourceStore == null)
        {
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace("Skipped AI data-source alignment because no '{ServiceType}' implementation was registered.", nameof(IAIDataSourceStore));
            }

            return;
        }

        var dataSources = await dataSourceStore.GetAllAsync(cancellationToken);
        var dataSourceList = dataSources?.ToList() ?? [];
        if (dataSourceList.Count == 0)
        {
            _logger.LogTrace("Skipped AI data-source alignment because no AI data sources were configured.");

            return;
        }

        var indexingService = services.GetRequiredService<IAIDataSourceIndexingService>();

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Starting nightly data-source alignment for {Count} data source(s).", dataSourceList.Count);
        }

        await indexingService.SyncAllAsync(cancellationToken);

        _logger.LogInformation("Nightly data-source alignment completed.");
    }
}
