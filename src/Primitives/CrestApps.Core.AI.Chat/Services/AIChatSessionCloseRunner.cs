using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Chat.Services;

/// <summary>
/// Runs the shared AI chat session close cycle on startup and then at a fixed interval.
/// Hosts that do not use <see cref="Microsoft.Extensions.Hosting.BackgroundService"/> can
/// reuse this service directly by calling <see cref="StartAsync"/> and <see cref="StopAsync"/>.
/// </summary>
public sealed class AIChatSessionCloseRunner
{
    private static readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    private readonly AIChatSessionCloseCycleService _cycleService;
    private readonly ILogger<AIChatSessionCloseRunner> _logger;
    private readonly Lock _syncLock = new();

    private CancellationTokenSource _stoppingTokenSource;
    private Task _executingTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIChatSessionCloseRunner"/> class.
    /// </summary>
    /// <param name="cycleService">The shared cycle service.</param>
    /// <param name="logger">The logger.</param>
    public AIChatSessionCloseRunner(
        AIChatSessionCloseCycleService cycleService,
        ILogger<AIChatSessionCloseRunner> logger)
    {
        _cycleService = cycleService;
        _logger = logger;
    }

    /// <summary>
    /// Starts the recurring AI chat session close runner.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncLock)
        {
            if (_executingTask is { IsCompleted: false })
            {
                return Task.CompletedTask;
            }

            _stoppingTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _executingTask = Task.Run(() => RunAsync(_stoppingTokenSource.Token), CancellationToken.None);
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "AI chat session close runner started. Interval: {Interval}.",
                _interval);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the recurring AI chat session close runner.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task executingTask;
        CancellationTokenSource stoppingTokenSource;

        lock (_syncLock)
        {
            executingTask = _executingTask;
            stoppingTokenSource = _stoppingTokenSource;
            _executingTask = null;
            _stoppingTokenSource = null;
        }

        if (executingTask is null || stoppingTokenSource is null)
        {
            return;
        }

        stoppingTokenSource.Cancel();

        try
        {
            await executingTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (stoppingTokenSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            stoppingTokenSource.Dispose();
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("AI chat session close runner stopped.");
        }
    }

    /// <summary>
    /// Runs the shared AI chat session close cycle immediately.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        return _cycleService.RunOnceAsync(cancellationToken);
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        await _cycleService.RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await _cycleService.RunOnceAsync(stoppingToken);
        }
    }
}
