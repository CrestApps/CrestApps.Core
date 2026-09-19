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

    /// <summary>
    /// Adds transport type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="configure">The configure.</param>
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
