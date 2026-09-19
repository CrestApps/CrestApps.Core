using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Mcp.Models;

/// <summary>
/// Represents the MCP Client Transport Entry.
/// </summary>
public sealed class McpClientTransportEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpClientTransportEntry"/> class.
    /// </summary>
    /// <param name="type">The type.</param>
    public McpClientTransportEntry(string type)
    {
        Type = type;
    }

    /// <summary>
    /// Gets or sets the type.
    /// </summary>
    public string Type { get; private set; }

    /// <summary>
    /// Gets or sets the display Name.
    /// </summary>
    public LocalizedString DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public LocalizedString Description { get; set; }
}
