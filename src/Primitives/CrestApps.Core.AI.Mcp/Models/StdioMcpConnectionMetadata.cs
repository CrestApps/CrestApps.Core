namespace CrestApps.Core.AI.Mcp.Models;

/// <summary>
/// Represents the stdio MCP Connection Metadata.
/// </summary>
public sealed class StdioMcpConnectionMetadata
{
    /// <summary>
    /// Gets or sets the command.
    /// </summary>
    public string Command { get; set; }

    /// <summary>
    /// Gets or sets the arguments.
    /// </summary>
    public string[] Arguments { get; set; }

    /// <summary>
    /// Gets or sets the working Directory.
    /// </summary>
    public string WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets the environment Variables.
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; }
}
