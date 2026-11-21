namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Describes a registered AI tool definition, including its CLR type, display metadata, and runtime attributes.
/// </summary>
public sealed class AIToolDefinitionEntry
{
    private readonly HashSet<string> _dependencies = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _accessAliases = new(StringComparer.OrdinalIgnoreCase);
    private bool? _requiresApproval;

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
    /// Gets or sets what the tool does to the application's data and to the outside world. The default,
    /// <see cref="AIToolKind.Unspecified"/>, keeps the behavior of a tool registered without a kind.
    /// </summary>
    public AIToolKind Kind { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a person must approve each call of this tool before it runs,
    /// when the caller can ask for an approval (such as an AG-UI chat run). Set it on tools that change or delete
    /// data, send messages, or have other effects that are hard to undo.
    /// </summary>
    /// <remarks>
    /// Until a value is set, it follows from <see cref="Kind"/>: a <see cref="AIToolKind.Destructive"/>,
    /// <see cref="AIToolKind.External"/> or <see cref="AIToolKind.Security"/> tool requires an approval and any
    /// other tool does not. A value that is set wins over the kind, whichever was set first.
    /// </remarks>
    public bool RequiresApproval
    {
        get => _requiresApproval ?? Kind.RequiresApproval();
        set => _requiresApproval = value;
    }

    /// <summary>
    /// Gets the registered tool dependencies that should be included when this tool is selected.
    /// Missing dependencies are ignored by dependency expansion helpers.
    /// </summary>
    public IReadOnlyCollection<string> Dependencies => _dependencies;

    /// <summary>
    /// Gets the names this tool was registered under before, or is also known by. A name stored on a profile, a
    /// template or a setting that matches an alias resolves to this tool. Listings show the tool once, under
    /// <see cref="Name"/>.
    /// </summary>
    /// <remarks>
    /// Aliases are read when the tool definitions are built, so add them while the tool is registered.
    /// </remarks>
    public IReadOnlyCollection<string> Aliases => _aliases;

    /// <summary>
    /// Gets the aliases that also carry access: a user the access check authorizes for one of these names may use
    /// this tool. Only an alias added with <c>grantsAccess</c> is here, because only a rename keeps what the tool
    /// does; a narrower tool merged into a broader one must not hand its grants to the broader one.
    /// </summary>
    public IReadOnlyCollection<string> AccessAliases => _accessAliases;

    /// <summary>
    /// Adds a name this tool was registered under before, so stored references to that name keep resolving to it.
    /// An alias that equals <see cref="Name"/> is ignored.
    /// </summary>
    /// <param name="alias">The other name of the tool.</param>
    /// <param name="grantsAccess">
    /// Whether access granted for the alias also grants access to this tool. Pass <see langword="true"/> for a
    /// rename, where the tool does what the earlier name did. Leave it <see langword="false"/> when a narrower tool
    /// was merged into this one, so the earlier tool's grants do not reach what this tool can also do.
    /// </param>
    /// <returns><see langword="true"/> when the alias was added; otherwise <see langword="false"/>.</returns>
    public bool AddAlias(string alias, bool grantsAccess = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        alias = alias.Trim();

        if (string.Equals(alias, Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (grantsAccess)
        {
            _accessAliases.Add(alias);
        }

        return _aliases.Add(alias);
    }

    /// <summary>
    /// Determines whether the tool is registered under the given name, as its <see cref="Name"/> or as one of its
    /// <see cref="Aliases"/>. Names are compared without regard to case.
    /// </summary>
    /// <param name="name">The name to look for.</param>
    /// <returns><see langword="true"/> when the name is the tool's name or one of its aliases.</returns>
    public bool IsKnownAs(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return string.Equals(name, Name, StringComparison.OrdinalIgnoreCase) || _aliases.Contains(name);
    }

    /// <summary>
    /// Determines whether a set of tool names, such as an allow-list stored in a setting, names this tool by its
    /// <see cref="Name"/> or by one of its <see cref="Aliases"/>.
    /// </summary>
    /// <param name="names">The tool names. Use a set that compares names without regard to case.</param>
    /// <returns><see langword="true"/> when the set holds the tool's name or one of its aliases.</returns>
    public bool IsNamedIn(ISet<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        if (!string.IsNullOrEmpty(Name) && names.Contains(Name))
        {
            return true;
        }

        foreach (var alias in _aliases)
        {
            if (names.Contains(alias))
            {
                return true;
            }
        }

        return false;
    }

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
