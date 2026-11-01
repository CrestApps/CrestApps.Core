namespace CrestApps.Core.AI.WebMcp;

/// <summary>
/// Provides the constants of the WebMCP services.
/// </summary>
public static class WebMcpConstants
{
    /// <summary>
    /// The default route prefix of the WebMCP tool endpoints.
    /// </summary>
    public const string DefaultRoutePrefix = "api/ai/webmcp";

    /// <summary>
    /// The client method the chat script handles to run a page tool. The server calls it with SignalR client results
    /// and waits for the <see cref="WebMcpPageToolResult"/> it returns.
    /// </summary>
    public const string ExecutePageToolMethod = "ExecutePageTool";

    /// <summary>
    /// The id of the JSON block that carries the page configuration to the browser scripts.
    /// </summary>
    public const string ConfigurationElementId = "crestapps-webmcp-config";
}
