using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Mcp.Models;

/// <summary>
/// Represents the MCP Options.
/// </summary>
public sealed class McpOptions
{
    private readonly Dictionary<string, McpResourceTypeEntry> _resourceTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the resource Types.
    /// </summary>
    public IReadOnlyDictionary<string, McpResourceTypeEntry> ResourceTypes
    {
        get
        {
            return _resourceTypes;
        }
    }

    /// <summary>
    /// Adds resource type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="configure">The configure.</param>
    public void AddResourceType(string type, Action<McpResourceTypeEntry> configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        if (!_resourceTypes.TryGetValue(type, out var entry))
        {
            entry = new McpResourceTypeEntry(type);
        }

        if (configure != null)
        {
            configure(entry);
        }

        if (string.IsNullOrEmpty(entry.DisplayName))
        {
            entry.DisplayName = new LocalizedString(type, type);
        }

        _resourceTypes[type] = entry;
    }
}
