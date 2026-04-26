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

    internal void SetTool(string name, AIToolDefinitionEntry entry)
    {
        _tools[name] = entry;
    }
}
