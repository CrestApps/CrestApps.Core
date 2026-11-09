using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Mcp;

/// <summary>
/// The logs of a tool call proxied to an MCP server. They run on every MCP tool call, so they are generated rather
/// than formatted at run time, and each has an event id to filter or alert on. The ids sit in the MCP and A2A range
/// (7000s).
/// </summary>
internal static partial class McpLog
{
    [LoggerMessage(EventId = 7101, Level = LogLevel.Debug, Message = "AI tool '{ToolName}' invoked.")]
    public static partial void ToolInvoked(ILogger logger, string toolName);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Warning, Message = "AI tool '{ToolName}' failed: MCP connection '{ConnectionId}' not found.")]
    public static partial void ConnectionNotFound(ILogger logger, string toolName, string connectionId);

    [LoggerMessage(EventId = 7103, Level = LogLevel.Warning, Message = "AI tool '{ToolName}' failed: could not connect to MCP server '{ConnectionId}'.")]
    public static partial void ConnectFailed(ILogger logger, string toolName, string connectionId);

    [LoggerMessage(EventId = 7104, Level = LogLevel.Debug, Message = "AI tool '{ToolName}' completed.")]
    public static partial void ToolCompleted(ILogger logger, string toolName);

    [LoggerMessage(EventId = 7105, Level = LogLevel.Error, Message = "Error invoking MCP tool '{ToolName}' on server '{ConnectionId}'.")]
    public static partial void ToolFailed(ILogger logger, Exception exception, string toolName, string connectionId);
}
