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

        return this;
    }
}
