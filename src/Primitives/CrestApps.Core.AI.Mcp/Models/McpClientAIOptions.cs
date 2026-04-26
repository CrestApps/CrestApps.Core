using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Mcp.Models;

/// <summary>
/// Represents the MCP Client AI Options.
/// </summary>
public sealed class McpClientAIOptions
{
    private readonly Dictionary<string, McpClientTransportEntry> _transportTypes = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets the transport Types.
    /// </summary>
    public IReadOnlyDictionary<string, McpClientTransportEntry> TransportTypes
    {
        get
        {
            return _transportTypes;
        }
    }

    public void AddTransportType(string type, Action<McpClientTransportEntry> configure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        if (!_transportTypes.TryGetValue(type, out var entry))
        {
            entry = new McpClientTransportEntry(type);
        }

        if (configure != null)
        {
            configure(entry);
        }

        if (string.IsNullOrEmpty(entry.DisplayName))
        {
            entry.DisplayName = new LocalizedString(type, type);
        }

        _transportTypes[type] = entry;
    }
}

/// <summary>
/// Represents the MCP Client Transport Entry.
/// </summary>
public sealed class McpClientTransportEntry
{
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
