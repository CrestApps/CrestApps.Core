using System.Collections.Concurrent;

namespace CrestApps.Core.AI.WebMcp.Services;

/// <summary>
/// Keeps the page tools of each chat connection in memory. A connection lives on one server, and the completions of
/// the messages it sends run on that server too, so the registry needs no shared store in a web farm.
/// </summary>
public sealed class DefaultWebMcpPageToolRegistry : IWebMcpPageToolRegistry
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<WebMcpPageTool>> _tools = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public int Register(string connectionId, IEnumerable<WebMcpPageToolDescriptor> tools)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);

        var validTools = WebMcpPageToolValidator.Validate(tools);

        if (validTools.Count == 0)
        {
            _tools.TryRemove(connectionId, out _);

            return 0;
        }

        _tools[connectionId] = validTools;

        return validTools.Count;
    }

    /// <inheritdoc/>
    public void Remove(string connectionId)
    {
        if (!string.IsNullOrEmpty(connectionId))
        {
            _tools.TryRemove(connectionId, out _);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<WebMcpPageTool> GetTools(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId) || !_tools.TryGetValue(connectionId, out var tools))
        {
            return [];
        }

        return tools;
    }
}
