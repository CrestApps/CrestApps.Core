namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Options that hold the registered <see cref="AIToolDefinitionEntry"/> instances indexed by tool name.
/// </summary>
public sealed class AIToolDefinitionOptions
{
    private readonly Dictionary<string, AIToolDefinitionEntry> _tools = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a read-only dictionary of registered AI tool definitions, keyed by tool name.
    /// </summary>
    public IReadOnlyDictionary<string, AIToolDefinitionEntry> Tools => _tools;

    /// <summary>
    /// Expands the provided tool names to include any registered dependencies that are also available.
    /// Missing dependencies are ignored, and duplicate tool names are removed while preserving encounter order.
    /// </summary>
    /// <param name="toolNames">The initially requested tool names.</param>
    /// <returns>The requested tool names plus any available dependencies.</returns>
    public IReadOnlyList<string> ExpandToolNames(IEnumerable<string> toolNames)
    {
        ArgumentNullException.ThrowIfNull(toolNames);

        var expandedToolNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var toolName in toolNames)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                continue;
            }

            AddToolWithDependencies(toolName, seen, expandedToolNames);
        }

        return expandedToolNames;
    }

    internal void SetTool(string name, AIToolDefinitionEntry entry)
    {
        _tools[name] = entry;
    }

    private void AddToolWithDependencies(
        string toolName,
        ISet<string> seen,
        ICollection<string> expandedToolNames)
    {
        if (!seen.Add(toolName))
        {
            return;
        }

        expandedToolNames.Add(toolName);

        if (!_tools.TryGetValue(toolName, out var definition))
        {
            return;
        }

        foreach (var dependency in definition.Dependencies)
        {
            if (_tools.ContainsKey(dependency))
            {
                AddToolWithDependencies(dependency, seen, expandedToolNames);
            }
        }
    }
}
