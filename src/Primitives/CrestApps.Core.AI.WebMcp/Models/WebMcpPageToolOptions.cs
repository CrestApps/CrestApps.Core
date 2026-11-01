namespace CrestApps.Core.AI.WebMcp.Models;

/// <summary>
/// The options of the page tools the AI chat calls in the user's browser tab.
/// </summary>
public sealed class WebMcpPageToolOptions
{
    /// <summary>
    /// Gets or sets how long the chat waits for the browser to answer a page tool call. The default is 60 seconds.
    /// </summary>
    public TimeSpan ToolCallTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
