namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Describes a registered AI tool definition, including its CLR type, display metadata, and runtime attributes.
/// </summary>
public sealed class AIToolDefinitionEntry
{
    private readonly HashSet<string> _dependencies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="AIToolDefinitionEntry"/> class.
    /// </summary>
    /// <param name="type">The type.</param>
    public AIToolDefinitionEntry(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        ToolType = type;
    }

    /// <summary>
    /// Gets the CLR type of the AI tool.
    /// </summary>
    public Type ToolType { get; }

    /// <summary>
    /// Gets or sets the display title for this tool.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets a description of the tool's capabilities.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the category for grouping this tool in the UI.
    /// </summary>
    public string Category { get; set; }

    /// <summary>
    /// Gets the registered technical name of this tool.
    /// </summary>
    public string Name { get; internal set; }

    /// <summary>
    /// Gets or sets whether this tool is a system tool. System tools are automatically
    /// included by the orchestrator based on context availability and are not shown
    /// in the UI tool selection.
    /// </summary>
    public bool IsSystemTool { get; set; }

    /// <summary>
    /// Gets or sets whether this tool is hidden from the user-facing tool picker. Hidden tools are
    /// not system tools — they are not auto-included by the orchestrator — but can still be
    /// referenced by name from a profile (for example by a system agent), so they never appear in
    /// the selectable tool list while remaining available to the profiles that opt into them.
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Gets or sets the purpose tag for this tool. Use well-known constants from
    /// <see cref="AIToolPurposes"/> or custom strings for domain-specific grouping.
    /// The orchestrator uses this to dynamically discover tools by purpose
    /// (e.g., document processing tools for enriching system messages).
    /// </summary>
    public string Purpose { get; set; }

    /// <summary>
    /// Gets the registered tool dependencies that should be included when this tool is selected.
    /// Missing dependencies are ignored by dependency expansion helpers.
    /// </summary>
    public IReadOnlyCollection<string> Dependencies => _dependencies;

    /// <summary>
    /// Adds a tool dependency that should be included when this tool is selected.
    /// </summary>
    /// <param name="toolName">The registered tool name of the dependency.</param>
    /// <returns><see langword="true"/> when the dependency was added; otherwise <see langword="false"/>.</returns>
    public bool AddDependency(string toolName)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        return _dependencies.Add(toolName);
    }

    /// <summary>
    /// Removes a previously registered tool dependency.
    /// </summary>
    /// <param name="toolName">The registered tool name of the dependency to remove.</param>
    /// <returns><see langword="true"/> when the dependency was removed; otherwise <see langword="false"/>.</returns>
    public bool RemoveDependency(string toolName)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        return _dependencies.Remove(toolName);
    }

    /// <summary>
    /// Determines whether purpose.
    /// </summary>
    /// <param name="purpose">The purpose.</param>
    public bool HasPurpose(string purpose)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);

        if (Purpose == null)
        {
            return false;
        }

        return string.Equals(Purpose, purpose, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether the tool is selectable in user-facing tool pickers.
    /// </summary>
    /// <returns><see langword="true"/> when the tool is neither a system tool nor hidden; otherwise <see langword="false"/>.</returns>
    public bool IsSelectable()
    {
        return !IsSystemTool && !Hidden;
    }
}
