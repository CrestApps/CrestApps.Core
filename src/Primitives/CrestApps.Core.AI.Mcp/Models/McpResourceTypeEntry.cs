using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Mcp.Models;

/// <summary>
/// Represents the MCP Resource Type Entry.
/// </summary>
public sealed class McpResourceTypeEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpResourceTypeEntry"/> class.
    /// </summary>
    /// <param name="type">The type.</param>
    public McpResourceTypeEntry(string type)
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

    /// <summary>
    /// Gets or sets the variables supported by this resource type.
    /// These are displayed in the UI to help users understand what variables can be used in URI patterns.
    /// </summary>
    public McpResourceVariable[] SupportedVariables { get; set; } = [];
}
