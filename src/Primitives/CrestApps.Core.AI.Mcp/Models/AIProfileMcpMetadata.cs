namespace CrestApps.Core.AI.Mcp.Models;

/// <summary>
/// Represents the AI Profile MCP Metadata.
/// </summary>
public sealed class AIProfileMcpMetadata
{
    /// <summary>
    /// Gets or sets the identifiers of the MCP connections the profile may use.
    /// </summary>
    public string[] ConnectionIds { get; set; }

    /// <summary>
    /// Gets or sets, per connection, the names of the tools the profile may use from it. A connection with no
    /// entry — or a <see langword="null"/> entry — exposes every tool it offers, which is what every profile did
    /// before this existed and what a profile saved by an older host still does. An empty array exposes none:
    /// the connection is kept for its prompts and resources, but contributes no tools.
    /// </summary>
    /// <remarks>
    /// An MCP server routinely offers dozens of tools, and a profile — a voice profile above all — is only as
    /// reliable as its tool list is short. This lets a profile take the three it needs from a server that
    /// offers forty, instead of taking the connection whole.
    /// </remarks>
    public Dictionary<string, string[]> ToolNames { get; set; }
}
