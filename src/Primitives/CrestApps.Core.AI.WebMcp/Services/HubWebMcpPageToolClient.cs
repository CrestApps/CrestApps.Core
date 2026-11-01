using CrestApps.Core.AI.WebMcp.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.WebMcp.Services;

/// <summary>
/// Runs page tools in a chat connection's browser tab with SignalR client results: the server invokes
/// <see cref="WebMcpConstants.ExecutePageToolMethod"/> on the connection and waits for the result the tab returns.
/// </summary>
/// <typeparam name="THub">The chat hub the browser tab is connected to.</typeparam>
public sealed class HubWebMcpPageToolClient<THub> : IWebMcpPageToolClient
    where THub : Hub
{
    private readonly IHubContext<THub> _hubContext;
    private readonly WebMcpPageToolOptions _options;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HubWebMcpPageToolClient{THub}"/> class.
    /// </summary>
    /// <param name="hubContext">The context of the chat hub.</param>
    /// <param name="options">The page tool options.</param>
    /// <param name="logger">The logger.</param>
    public HubWebMcpPageToolClient(
        IHubContext<THub> hubContext,
        IOptions<WebMcpPageToolOptions> options,
        ILogger<HubWebMcpPageToolClient<THub>> logger)
    {
        _hubContext = hubContext;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<WebMcpPageToolResult> ExecuteAsync(string connectionId, string toolName, string argumentsJson, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(_options.ToolCallTimeout);

        try
        {
            var result = await _hubContext.Clients.Client(connectionId).InvokeAsync<WebMcpPageToolResult>(
                WebMcpConstants.ExecutePageToolMethod,
                toolName,
                string.IsNullOrEmpty(argumentsJson) ? "{}" : argumentsJson,
                timeout.Token);

            return result ?? WebMcpPageToolResult.Failure("The page answered nothing.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return WebMcpPageToolResult.Failure("The page did not answer in time.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "The page tool '{ToolName}' could not run in the browser.", toolName);

            return WebMcpPageToolResult.Failure("The page that offers this tool could not run it.");
        }
    }
}
