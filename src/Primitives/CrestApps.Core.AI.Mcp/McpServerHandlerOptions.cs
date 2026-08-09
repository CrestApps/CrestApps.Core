namespace CrestApps.Core.AI.Mcp;

/// <summary>
/// Strongly-typed options that control which CrestApps MCP server capabilities are exposed. Bind this
/// type from configuration (for example an <c>Mcp:Server:Handlers</c> section) so a host can enable or
/// disable capabilities without code. Each toggle is nullable: a <see langword="null"/> value means the
/// setting is not configured and the value chosen in code is used instead.
/// </summary>
public sealed class McpServerHandlerOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the tool list and call handlers are registered. When
    /// <see langword="null"/> the value configured in code is used.
    /// </summary>
    public bool? IncludeTools { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether SDK tool instances registered in the service provider
    /// are merged into the exposed tool set. This only applies when <see cref="IncludeTools"/> resolves
    /// to <see langword="true"/>. When <see langword="null"/> the value configured in code is used.
    /// </summary>
    public bool? IncludeSdkTools { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the prompt list and get handlers are registered. When
    /// <see langword="null"/> the value configured in code is used.
    /// </summary>
    public bool? IncludePrompts { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the resource list, template list, and read handlers are
    /// registered. When <see langword="null"/> the value configured in code is used.
    /// </summary>
    public bool? IncludeResources { get; set; }
}
