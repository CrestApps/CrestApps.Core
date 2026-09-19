using CrestApps.Core.AI.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// A hosted timer that periodically asks <see cref="IFileSourceScheduler"/> to run whatever is due.
/// </summary>
/// <remarks>
/// Everything this knows is when to wake up. Deciding what is due and running it belongs to
/// <see cref="IFileSourceScheduler"/>, so a host that schedules its own work — Orchard Core's background
/// tasks, a cron job, an operator pressing a button — resolves that and never takes this class or its timer.
/// <para>
/// A host that does its own scheduling should leave this unregistered, or the same sources are driven twice.
/// </para>
/// </remarks>
internal sealed class FileSourceBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly FileSourceOptions _options;
    private readonly ILogger<FileSourceBackgroundService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSourceBackgroundService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="options">The file source options.</param>
    /// <param name="logger">The logger.</param>
    public FileSourceBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<FileSourceOptions> options,
        ILogger<FileSourceBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.RunCheckIntervalMinutes));
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
                await RunDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while running file sources.");
            }
        }
    }

    /// <summary>
    /// Opens a scope and hands the work to the scheduler.
    /// </summary>
    /// <param name="stoppingToken">The stopping token.</param>
    /// <remarks>
    /// The scheduler is scoped and creates no scope of its own, so this is where one is opened for the pass.
    /// </remarks>
    private async Task RunDueAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();

        var scheduler = scope.ServiceProvider.GetService<IFileSourceScheduler>();

        if (scheduler is null)
        {
            return;
        }

        await scheduler.RunDueAsync(stoppingToken);
    }
}
