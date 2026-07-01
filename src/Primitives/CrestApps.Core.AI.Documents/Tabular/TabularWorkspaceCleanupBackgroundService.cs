using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Tabular;

internal sealed class TabularWorkspaceCleanupBackgroundService : BackgroundService
{
    private readonly TabularWorkspaceCache _cache;
    private readonly TabularWorkspaceOptions _options;
    private readonly ILogger<TabularWorkspaceCleanupBackgroundService> _logger;

    public TabularWorkspaceCleanupBackgroundService(
        TabularWorkspaceCache cache,
        IOptions<TabularWorkspaceOptions> options,
        ILogger<TabularWorkspaceCleanupBackgroundService> logger)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ResolveCleanupInterval());

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var removed = _cache.CompactExpired();

            if (removed > 0 && _logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Disposed {WorkspaceCount} expired tabular workspace(s).", removed);
            }
        }
    }

    private TimeSpan ResolveCleanupInterval()
    {
        return _options.WorkspaceCleanupInterval <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(1)
            : _options.WorkspaceCleanupInterval;
    }
}
