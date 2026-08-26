namespace CrestApps.Core.AI.Mcp.Models;

/// <summary>
/// Represents a single message that makes up the body of an MCP catalog prompt.
/// </summary>
public sealed class McpPromptMessage
{
    /// <summary>
    /// Gets or sets the role of the message. Only "user" and "assistant" are supported by MCP prompts.
    /// </summary>
    public string Role { get; set; }

    /// <summary>
    /// Gets or sets the textual content of the message. May contain <c>{{argName}}</c> placeholders
    /// that are substituted with the supplied argument values when the prompt is requested.
    /// </summary>
    public string Content { get; set; }
}
