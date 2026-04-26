using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Mcp.Models;

/// <summary>
/// Describes a variable that a resource type handler supports.
/// Users can include these variables (wrapped in braces) in their URI patterns.
/// </summary>
public sealed class McpResourceVariable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpResourceVariable"/> class.
    /// </summary>
    /// <param name="name">The name.</param>
    public McpResourceVariable(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        Name = name;
    }

    /// <summary>
    /// Gets the variable name (e.g., "path", "stepName").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets a description of the variable displayed in the UI.
    /// </summary>
    public LocalizedString Description { get; set; }
}
