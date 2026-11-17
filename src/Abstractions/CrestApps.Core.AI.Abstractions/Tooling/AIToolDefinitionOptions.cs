namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Options that hold the registered <see cref="AIToolDefinitionEntry"/> instances indexed by tool name.
/// </summary>
public sealed class AIToolDefinitionOptions
{
    private readonly Dictionary<string, AIToolDefinitionEntry> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a read-only dictionary of registered AI tool definitions, keyed by tool name. Each tool is listed
    /// once, under its name; its aliases are not keys. Use <see cref="TryGetTool"/> to look a tool up by a name
    /// that may be an alias.
    /// </summary>
    public IReadOnlyDictionary<string, AIToolDefinitionEntry> Tools => _tools;

    /// <summary>
    /// Gets the aliases of the registered tools, each with the name of the tool it belongs to.
    /// </summary>
    public IReadOnlyDictionary<string, string> Aliases => _aliases;

    /// <summary>
    /// Gets the definition of the tool registered under the given name, or under an alias equal to it.
    /// </summary>
    /// <param name="nameOrAlias">The name of the tool, or one of its aliases.</param>
    /// <param name="definition">The definition of the tool, when one is found.</param>
    /// <returns><see langword="true"/> when a tool is registered under the name or the alias.</returns>
    public bool TryGetTool(string nameOrAlias, out AIToolDefinitionEntry definition)
    {
        definition = null;

        if (string.IsNullOrEmpty(nameOrAlias))
        {
            return false;
        }

        if (_tools.TryGetValue(nameOrAlias, out definition))
        {
            return true;
        }

        return _aliases.TryGetValue(nameOrAlias, out var toolName) && _tools.TryGetValue(toolName, out definition);
    }

    /// <summary>
    /// Gets the name the tool is registered under, for a name that may be one of its aliases.
    /// </summary>
    /// <param name="nameOrAlias">The name of the tool, or one of its aliases.</param>
    /// <returns>
    /// The registered name of the tool, or <paramref name="nameOrAlias"/> unchanged when no tool is registered
    /// under it.
    /// </returns>
    public string ResolveToolName(string nameOrAlias)
    {
        if (string.IsNullOrEmpty(nameOrAlias) || _tools.ContainsKey(nameOrAlias))
        {
            return nameOrAlias;
        }

        return _aliases.TryGetValue(nameOrAlias, out var toolName)
            ? toolName
            : nameOrAlias;
    }

    /// <summary>
    /// Replaces each alias in the provided names with the name of the tool it belongs to. Empty names and
    /// duplicates are removed while preserving encounter order, and a name no tool is registered under is kept
    /// as it is.
    /// </summary>
    /// <param name="toolNames">The tool names, such as the names stored on a profile.</param>
    /// <returns>The names the tools are registered under.</returns>
    public IReadOnlyList<string> ResolveToolNames(IEnumerable<string> toolNames)
    {
        ArgumentNullException.ThrowIfNull(toolNames);

        var resolvedToolNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var toolName in toolNames)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                continue;
            }

            var resolvedToolName = ResolveToolName(toolName);

            if (seen.Add(resolvedToolName))
            {
                resolvedToolNames.Add(resolvedToolName);
            }
        }

        return resolvedToolNames;
    }

    /// <summary>
    /// Expands the provided tool names to include any registered dependencies that are also available.
    /// Missing dependencies are ignored, and duplicate tool names are removed while preserving encounter order.
    /// A name that is an alias is replaced with the name of the tool it belongs to.
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

            AddToolWithDependencies(ResolveToolName(toolName), seen, expandedToolNames);
        }

        return expandedToolNames;
    }

    /// <summary>
    /// Gets the registered tools that are selectable in user-facing tool pickers.
    /// </summary>
    /// <returns>A read-only dictionary of selectable AI tool definitions keyed by tool name.</returns>
    public IReadOnlyDictionary<string, AIToolDefinitionEntry> GetSelectableTools()
    {
        return _tools
            .Where(tool => tool.Value.IsSelectable())
            .ToDictionary(tool => tool.Key, tool => tool.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Registers a tool under its name and its aliases. Registering a name again replaces the tool and the aliases
    /// the earlier registration added.
    /// </summary>
    /// <param name="name">The name of the tool.</param>
    /// <param name="entry">The definition of the tool.</param>
    /// <exception cref="InvalidOperationException">
    /// The name is an alias of another tool, or one of the aliases is the name or an alias of another tool.
    /// </exception>
    internal void SetTool(string name, AIToolDefinitionEntry entry)
    {
        if (_aliases.TryGetValue(name, out var aliasOwner))
        {
            throw new InvalidOperationException(
                $"The AI tool '{name}' cannot be registered because '{name}' is already an alias of the AI tool '{aliasOwner}'. An alias must not be the name or an alias of another tool.");
        }

        foreach (var alias in entry.Aliases)
        {
            if (string.Equals(alias, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (_tools.ContainsKey(alias))
            {
                throw new InvalidOperationException(
                    $"The alias '{alias}' of the AI tool '{name}' is the name of another AI tool. An alias must not be the name or an alias of another tool.");
            }

            if (_aliases.TryGetValue(alias, out var otherOwner) && !string.Equals(otherOwner, name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The alias '{alias}' of the AI tool '{name}' is already an alias of the AI tool '{otherOwner}'. An alias must not be the name or an alias of another tool.");
            }
        }

        // A tool that is registered again replaces the earlier registration, so the aliases of that one are dropped.
        var staleAliases = _aliases
            .Where(alias => string.Equals(alias.Value, name, StringComparison.OrdinalIgnoreCase))
            .Select(alias => alias.Key)
            .ToList();

        foreach (var staleAlias in staleAliases)
        {
            _aliases.Remove(staleAlias);
        }

        foreach (var alias in entry.Aliases)
        {
            if (!string.Equals(alias, name, StringComparison.OrdinalIgnoreCase))
            {
                _aliases[alias] = name;
            }
        }

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
            // A dependency may still be declared under the earlier name of a tool that was renamed.
            var dependencyName = ResolveToolName(dependency);

            if (_tools.ContainsKey(dependencyName))
            {
                AddToolWithDependencies(dependencyName, seen, expandedToolNames);
            }
        }
    }
}
