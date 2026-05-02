using Microsoft.Extensions.Hosting;

namespace CrestApps.Core.AI.Chat.Services;

/// <summary>
/// Default hosted-service wrapper for the reusable AI chat session close runner.
/// </summary>
public sealed class AIChatSessionCloseBackgroundService : IHostedService
{
    private readonly AIChatSessionCloseRunner _runner;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIChatSessionCloseBackgroundService"/> class.
    /// </summary>
    /// <param name="runner">The reusable session close runner.</param>
    public AIChatSessionCloseBackgroundService(AIChatSessionCloseRunner runner)
    {
        _runner = runner;
    }

    /// <summary>
    /// Starts the shared AI chat session close runner.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _runner.StartAsync(cancellationToken);
    }

    /// <summary>
    /// Stops the shared AI chat session close runner.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return _runner.StopAsync(cancellationToken);
    }
}
