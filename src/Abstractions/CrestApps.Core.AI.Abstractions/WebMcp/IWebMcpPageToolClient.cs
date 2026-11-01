namespace CrestApps.Core.AI.WebMcp;

/// <summary>
/// Runs a page tool in the browser tab of a chat connection and waits for its answer.
/// </summary>
public interface IWebMcpPageToolClient
{
    /// <summary>
    /// Asks a browser tab to run one of its page tools.
    /// </summary>
    /// <param name="connectionId">The SignalR connection of the browser tab.</param>
    /// <param name="toolName">The function name of the tool.</param>
    /// <param name="argumentsJson">The tool's arguments, as a JSON object.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// What the tab answered, or a failure when the tab cannot be reached, closes, or does not answer in time.
    /// </returns>
    Task<WebMcpPageToolResult> ExecuteAsync(string connectionId, string toolName, string argumentsJson, CancellationToken cancellationToken = default);
}
