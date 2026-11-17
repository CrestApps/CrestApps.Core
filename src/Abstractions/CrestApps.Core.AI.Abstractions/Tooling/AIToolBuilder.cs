using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// A fluent builder for configuring an AI tool registration.
/// By default, tools are registered as system tools (not visible in the UI).
/// Call <see cref="Selectable"/> to make the tool visible in the UI for user selection.
/// </summary>
/// <typeparam name="TTool">The tool type implementing <see cref="AITool"/>.</typeparam>
public sealed class AIToolBuilder<TTool>
    where TTool : AITool
{
    private readonly AIToolDefinitionEntry _entry;

    internal AIToolBuilder(AIToolDefinitionEntry entry)
    {
        _entry = entry;
    }

    /// <summary>
    /// Sets the display title for this tool.
    /// </summary>
    /// <param name="title">The title.</param>
    public AIToolBuilder<TTool> WithTitle(string title)
    {
        _entry.Title = title;

        return this;
    }

    /// <summary>
    /// Sets the description for this tool.
    /// </summary>
    /// <param name="description">The description.</param>
    public AIToolBuilder<TTool> WithDescription(string description)
    {
        _entry.Description = description;

        return this;
    }

    /// <summary>
    /// Sets the category for grouping this tool in the UI.
    /// </summary>
    /// <param name="category">The category.</param>
    public AIToolBuilder<TTool> WithCategory(string category)
    {
        _entry.Category = category;

        return this;
    }

    /// <summary>
    /// Sets the purpose tag for this tool. Use well-known constants from <see cref="AIToolPurposes"/>
    /// or define custom purpose strings for domain-specific tool grouping.
    /// </summary>
    /// <param name="purpose">The purpose.</param>
    public AIToolBuilder<TTool> WithPurpose(string purpose)
    {
        _entry.Purpose = purpose;

        return this;
    }

    /// <summary>
    /// Adds a tool dependency that should be included when this tool is selected.
    /// </summary>
    /// <param name="toolName">The registered tool name of the dependency.</param>
    public AIToolBuilder<TTool> WithDependency(string toolName)
    {
        _entry.AddDependency(toolName);

        return this;
    }

    /// <summary>
    /// Adds tool dependencies that should be included when this tool is selected.
    /// </summary>
    /// <param name="toolNames">The registered tool names of the dependencies.</param>
    public AIToolBuilder<TTool> WithDependencies(params string[] toolNames)
    {
        ArgumentNullException.ThrowIfNull(toolNames);

        foreach (var toolName in toolNames)
        {
            _entry.AddDependency(toolName);
        }

        return this;
    }

    /// <summary>
    /// Removes a previously registered tool dependency.
    /// </summary>
    /// <param name="toolName">The registered tool name of the dependency to remove.</param>
    public AIToolBuilder<TTool> WithoutDependency(string toolName)
    {
        _entry.RemoveDependency(toolName);

        return this;
    }

    /// <summary>
    /// Removes previously registered tool dependencies.
    /// </summary>
    /// <param name="toolNames">The registered tool names of the dependencies to remove.</param>
    public AIToolBuilder<TTool> WithoutDependencies(params string[] toolNames)
    {
        ArgumentNullException.ThrowIfNull(toolNames);

        foreach (var toolName in toolNames)
        {
            _entry.RemoveDependency(toolName);
        }

        return this;
    }

    /// <summary>
    /// Makes this tool visible in the UI for user selection.
    /// By default, tools are system tools managed by the orchestrator and are not shown in the UI.
    /// </summary>
    public AIToolBuilder<TTool> Selectable()
    {
        _entry.IsSystemTool = false;
        _entry.Hidden = false;

        return this;
    }

    /// <summary>
    /// Registers this tool so it can be referenced by name from a profile (for example by a system
    /// agent) but is hidden from the user-facing tool picker. Unlike a system tool, a hidden tool is
    /// not auto-included by the orchestrator; it is only included when a profile explicitly names it.
    /// </summary>
    public AIToolBuilder<TTool> Hidden()
    {
        _entry.IsSystemTool = false;
        _entry.Hidden = true;

        return this;
    }

    /// <summary>
    /// Asks a person to approve each call of this tool before it runs, when the caller can ask for an approval
    /// (such as an AG-UI chat run). Use it for tools that change or delete data, send messages, or have other
    /// effects that are hard to undo.
    /// </summary>
    /// <remarks>
    /// A tool marked <see cref="AsDestructive"/>, <see cref="AsExternal"/> or <see cref="AsSecurity"/> requires an
    /// approval without this call. Call it for the exceptions: to ask for an approval on a tool of another kind,
    /// or, with <see langword="false"/>, to run a tool of one of those kinds without asking.
    /// </remarks>
    /// <param name="requiresApproval">Whether each call needs an approval.</param>
    public AIToolBuilder<TTool> RequireApproval(bool requiresApproval = true)
    {
        _entry.RequiresApproval = requiresApproval;

        return this;
    }

    /// <summary>
    /// Sets what this tool does to the application's data and to the outside world.
    /// </summary>
    /// <param name="kind">The kind of the tool.</param>
    public AIToolBuilder<TTool> WithKind(AIToolKind kind)
    {
        _entry.Kind = kind;

        return this;
    }

    /// <summary>
    /// Marks this tool as one that returns data and changes nothing.
    /// </summary>
    public AIToolBuilder<TTool> AsRead()
    {
        return WithKind(AIToolKind.Read);
    }

    /// <summary>
    /// Marks this tool as one that creates or changes data that can be changed back.
    /// </summary>
    public AIToolBuilder<TTool> AsWrite()
    {
        return WithKind(AIToolKind.Write);
    }

    /// <summary>
    /// Marks this tool as one that deletes data or does something that cannot be undone. Each call then needs a
    /// person's approval, and a setting that exposes every tool leaves the tool out unless it names it.
    /// </summary>
    public AIToolBuilder<TTool> AsDestructive()
    {
        return WithKind(AIToolKind.Destructive);
    }

    /// <summary>
    /// Marks this tool as one that sends something outside the application, calls a paid service, or moves money.
    /// Each call then needs a person's approval.
    /// </summary>
    public AIToolBuilder<TTool> AsExternal()
    {
        return WithKind(AIToolKind.External);
    }

    /// <summary>
    /// Marks this tool as one that changes who can access what, how people sign in, secrets, or what code or
    /// templates the application runs. Each call then needs a person's approval, and a setting that exposes every
    /// tool leaves the tool out unless it names it.
    /// </summary>
    public AIToolBuilder<TTool> AsSecurity()
    {
        return WithKind(AIToolKind.Security);
    }

    /// <summary>
    /// Adds a name this tool was registered under before. A name stored on a profile, a template or a setting that
    /// matches the alias keeps resolving to this tool, and listings show the tool once, under its current name.
    /// Call it once for each earlier name.
    /// </summary>
    /// <remarks>
    /// An alias must not be the name or an alias of another tool: the tool definitions then fail to build, with a
    /// message that names both tools.
    /// </remarks>
    /// <param name="alias">The earlier name of the tool.</param>
    public AIToolBuilder<TTool> WithAlias(string alias)
    {
        _entry.AddAlias(alias);

        return this;
    }

    /// <summary>
    /// Adds names this tool was registered under before. See <see cref="WithAlias"/>.
    /// </summary>
    /// <param name="aliases">The earlier names of the tool.</param>
    public AIToolBuilder<TTool> WithAliases(params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);

        foreach (var alias in aliases)
        {
            _entry.AddAlias(alias);
        }

        return this;
    }
}
