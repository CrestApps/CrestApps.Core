namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Describes a registered AI tool definition, including its CLR type, display metadata, and runtime attributes.
/// </summary>
public sealed class AIToolDefinitionEntry
{
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
    /// Gets or sets the purpose tag for this tool. Use well-known constants from
    /// <see cref="AIToolPurposes"/> or custom strings for domain-specific grouping.
    /// The orchestrator uses this to dynamically discover tools by purpose
    /// (e.g., document processing tools for enriching system messages).
    /// </summary>
    public string Purpose { get; set; }

    public bool HasPurpose(string purpose)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);

        if (Purpose == null)
        {
            return false;
        }

        return string.Equals(Purpose, purpose, StringComparison.OrdinalIgnoreCase);
    }
}
