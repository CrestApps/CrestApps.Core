namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Holds the registered <see cref="AIToolInstanceDefinitionEntry"/> metadata for every
/// <see cref="IAIToolInstanceDefinition"/>, indexed by definition name.
/// </summary>
public sealed class AIToolInstanceDefinitionOptions
{
    private readonly Dictionary<string, AIToolInstanceDefinitionEntry> _definitions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a read-only dictionary of registered definition metadata, keyed by definition name.
    /// </summary>
    public IReadOnlyDictionary<string, AIToolInstanceDefinitionEntry> Definitions => _definitions;

    /// <summary>
    /// Attempts to resolve the metadata for the definition with the specified name.
    /// </summary>
    /// <param name="name">The definition name.</param>
    /// <param name="entry">When this method returns, contains the matching entry, if found.</param>
    /// <returns><see langword="true"/> when a matching entry was found; otherwise <see langword="false"/>.</returns>
    public bool TryGet(string name, out AIToolInstanceDefinitionEntry entry)
    {
        if (string.IsNullOrEmpty(name))
        {
            entry = null;

            return false;
        }

        return _definitions.TryGetValue(name, out entry);
    }

    internal void SetDefinition(string name, AIToolInstanceDefinitionEntry entry)
    {
        _definitions[name] = entry;
    }
}
