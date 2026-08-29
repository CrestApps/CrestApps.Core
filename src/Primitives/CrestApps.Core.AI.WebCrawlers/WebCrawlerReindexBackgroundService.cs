using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.WebCrawlers;

/// <summary>
/// A thin hosted job that periodically drives <see cref="IWebCrawlerReindexService.ReindexDueAsync"/>. All
/// of the re-index logic lives in the service, so a host that prefers its own scheduling (or a manual
/// trigger) can invoke the service directly without this background job.
/// </summary>
internal sealed class WebCrawlerReindexBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly WebCrawlerOptions _options;
    private readonly ILogger<WebCrawlerReindexBackgroundService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebCrawlerReindexBackgroundService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="options">The web-crawler options.</param>
    /// <param name="logger">The logger.</param>
    public WebCrawlerReindexBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<WebCrawlerOptions> options,
        ILogger<WebCrawlerReindexBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.ReindexCheckIntervalMinutes));
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
                var reindexService = scope.ServiceProvider.GetService<IWebCrawlerReindexService>();

                if (reindexService is null)
                {
                    continue;
                }

                await reindexService.ReindexDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while re-indexing web crawlers.");
            }
        }
    }
}
