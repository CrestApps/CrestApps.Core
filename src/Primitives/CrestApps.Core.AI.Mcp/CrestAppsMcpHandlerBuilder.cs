using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.AI.Mcp;

/// <summary>
/// Configures which CrestApps MCP protocol handlers are wired into an MCP server and,
/// for tools, which registered tools are exposed to external clients. By default every
/// capability (tools, prompts, and resources) is registered and all non-hidden tools are
/// exposed. Use the fluent methods to opt out of a capability or to narrow the set of tools.
/// </summary>
public sealed class CrestAppsMcpHandlerBuilder
{
    private List<Func<string, AIToolDefinitionEntry, bool>> _toolFilters;

    /// <summary>
    /// Gets a value indicating whether the tool list and call handlers are registered.
    /// </summary>
    public bool IncludeTools { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether SDK <c>McpServerTool</c> instances registered in the
    /// service provider are merged into the exposed tool set. This only applies when
    /// <see cref="IncludeTools"/> is <see langword="true"/>.
    /// </summary>
    public bool IncludeSdkTools { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the prompt list and get handlers are registered.
    /// </summary>
    public bool IncludePrompts { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the resource list, template list, and read handlers are registered.
    /// </summary>
    public bool IncludeResources { get; private set; } = true;

    /// <summary>
    /// Excludes the tool handlers so the server does not list or invoke any tools. Use this to expose
    /// a read-only knowledgebase server that only serves prompts and resources.
    /// </summary>
    /// <returns>The same builder instance for chaining.</returns>
    public CrestAppsMcpHandlerBuilder WithoutTools()
    {
        IncludeTools = false;

        return this;
    }

    /// <summary>
    /// Excludes SDK <c>McpServerTool</c> instances from the exposed tool set while keeping the
    /// CrestApps tool handlers registered.
    /// </summary>
    /// <returns>The same builder instance for chaining.</returns>
    public CrestAppsMcpHandlerBuilder WithoutSdkTools()
    {
        IncludeSdkTools = false;

        return this;
    }

    /// <summary>
    /// Excludes the prompt handlers so the server does not list or serve prompts.
    /// </summary>
    /// <returns>The same builder instance for chaining.</returns>
    public CrestAppsMcpHandlerBuilder WithoutPrompts()
    {
        IncludePrompts = false;

        return this;
    }

    /// <summary>
    /// Excludes the resource handlers so the server does not list, template, or read resources.
    /// </summary>
    /// <returns>The same builder instance for chaining.</returns>
    public CrestAppsMcpHandlerBuilder WithoutResources()
    {
        IncludeResources = false;

        return this;
    }

    /// <summary>
    /// Restricts the exposed tools to those matching the supplied predicate. Multiple filters are
    /// combined with logical AND, so a tool must satisfy every configured filter to be exposed.
    /// </summary>
    /// <param name="predicate">The predicate evaluated against each registered tool definition.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public CrestAppsMcpHandlerBuilder FilterTools(Func<AIToolDefinitionEntry, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        AddToolFilter((_, entry) => predicate(entry));

        return this;
    }

    /// <summary>
    /// Restricts the exposed tools to those assigned to one of the supplied categories. Multiple
    /// filters are combined with logical AND; the categories within this single call are combined
    /// with logical OR.
    /// </summary>
    /// <param name="categories">The categories to expose.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public CrestAppsMcpHandlerBuilder WithToolsInCategory(params string[] categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        AddToolFilter((_, entry) => entry.Category is not null
            && Array.Exists(categories, category => string.Equals(category, entry.Category, StringComparison.OrdinalIgnoreCase)));

        return this;
    }

    /// <summary>
    /// Restricts the exposed tools to those tagged with one of the supplied purposes. Multiple
    /// filters are combined with logical AND; the purposes within this single call are combined
    /// with logical OR. Use well-known constants from <see cref="AIToolPurposes"/> or custom strings.
    /// </summary>
    /// <param name="purposes">The purposes to expose.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public CrestAppsMcpHandlerBuilder WithToolsForPurpose(params string[] purposes)
    {
        ArgumentNullException.ThrowIfNull(purposes);

        AddToolFilter((_, entry) => Array.Exists(purposes, purpose => !string.IsNullOrEmpty(purpose) && entry.HasPurpose(purpose)));

        return this;
    }

    /// <summary>
    /// Restricts the exposed tools to those whose registered name matches one of the supplied names.
    /// Multiple filters are combined with logical AND; the names within this single call are combined
    /// with logical OR. Matching is ordinal and case-insensitive.
    /// </summary>
    /// <param name="names">The registered tool names to expose.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public CrestAppsMcpHandlerBuilder WithToolNames(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);

        AddToolFilter((name, entry) => Array.Exists(names, candidate =>
            string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)
            || (entry.Name is not null && string.Equals(candidate, entry.Name, StringComparison.OrdinalIgnoreCase))));

        return this;
    }

    /// <summary>
    /// Determines whether the tool with the supplied name and definition passes every configured filter.
    /// </summary>
    /// <param name="name">The registered tool name (the tool definition dictionary key).</param>
    /// <param name="entry">The tool definition being evaluated.</param>
    /// <returns><see langword="true"/> when the tool should be exposed; otherwise <see langword="false"/>.</returns>
    internal bool IsToolAllowed(string name, AIToolDefinitionEntry entry)
    {
        if (_toolFilters is null)
        {
            return true;
        }

        foreach (var filter in _toolFilters)
        {
            if (!filter(name, entry))
            {
                return false;
            }
        }

        return true;
    }

    private void AddToolFilter(Func<string, AIToolDefinitionEntry, bool> filter)
    {
        (_toolFilters ??= []).Add(filter);
    }
}
