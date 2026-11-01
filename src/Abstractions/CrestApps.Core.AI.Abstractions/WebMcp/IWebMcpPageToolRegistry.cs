namespace CrestApps.Core.AI.WebMcp;

/// <summary>
/// Remembers the page tools each chat connection's browser tab registered. The chat hub records them, and the tool
/// registry offers them to the completions of messages sent on that connection.
/// </summary>
public interface IWebMcpPageToolRegistry
{
    /// <summary>
    /// Checks the page tools a connection reports and records the valid ones in place of what it reported before.
    /// A report with no valid tool forgets the connection's tools.
    /// </summary>
    /// <param name="connectionId">The SignalR connection of the browser tab.</param>
    /// <param name="tools">The tools the tab reports.</param>
    /// <returns>How many tools were recorded.</returns>
    int Register(string connectionId, IEnumerable<WebMcpPageToolDescriptor> tools);

    /// <summary>
    /// Forgets the page tools of a connection.
    /// </summary>
    /// <param name="connectionId">The SignalR connection of the browser tab.</param>
    void Remove(string connectionId);

    /// <summary>
    /// Gets the page tools a connection registered.
    /// </summary>
    /// <param name="connectionId">The SignalR connection of the browser tab.</param>
    /// <returns>The tools, or an empty list when the connection registered none.</returns>
    IReadOnlyList<WebMcpPageTool> GetTools(string connectionId);
}
