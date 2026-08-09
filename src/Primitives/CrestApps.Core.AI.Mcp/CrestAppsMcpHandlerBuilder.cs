namespace CrestApps.Core.AI.Mcp;

/// <summary>
/// Configures which CrestApps MCP protocol handlers are wired into an MCP server. By default every
/// capability (tools, prompts, and resources) is registered. Use the fluent methods to opt out of a
/// capability, for example to expose a read-only knowledgebase server that only serves prompts and
/// resources. Which tools and tool instances are actually listed and callable is controlled separately
/// by the <see cref="Models.McpServerOptions"/> site settings allow-list.
/// </summary>
public sealed class CrestAppsMcpHandlerBuilder
{
    /// <summary>
    /// Gets a value indicating whether the tool list and call handlers are registered.
    /// </summary>
    public bool IncludeTools { get; private set; } = true;

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
}
