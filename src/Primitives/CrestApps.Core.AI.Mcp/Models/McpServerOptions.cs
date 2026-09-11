namespace CrestApps.Core.AI.Mcp.Models;

/// <summary>
/// Configuration options for the MCP server authentication and authorization.
/// </summary>
public sealed class McpServerOptions
{
    /// <summary>
    /// Gets or sets the authentication type to use for the MCP server.
    /// Default is <see cref="McpServerAuthenticationType.OpenId"/>.
    /// </summary>
    public McpServerAuthenticationType AuthenticationType { get; set; } = McpServerAuthenticationType.OpenId;

    /// <summary>
    /// Gets or sets the API key required for authentication when
    /// <see cref="AuthenticationType"/> is set to <see cref="McpServerAuthenticationType.ApiKey"/>.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets whether to require the <c>AccessMcpServer</c> permission.
    /// When set to <c>false</c>, any authenticated user can access the MCP server.
    /// Default is <c>true</c>.
    /// </summary>
    /// <remarks>
    /// This setting only applies when <see cref="AuthenticationType"/> is
    /// <see cref="McpServerAuthenticationType.OpenId"/>.
    /// </remarks>
    public bool RequireAccessPermission { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether every non-hidden tool and configured tool instance is
    /// exposed to MCP clients. When <c>false</c> (the default), the server exposes nothing unless a tool
    /// or tool instance is explicitly listed in <see cref="Tools"/>. When <c>true</c>, <see cref="Tools"/>
    /// is ignored and all non-hidden tools and instances are exposed.
    /// </summary>
    public bool ExposeAllTools { get; set; }

    /// <summary>
    /// Gets or sets the allow-list of tool names exposed to MCP clients when <see cref="ExposeAllTools"/>
    /// is <c>false</c>. Each entry matches a code-registered tool name, a tool instance's function name, or
    /// a tool instance's technical name. The server exposes nothing by default, so a tool or instance is
    /// only listed and callable when it appears here.
    /// </summary>
    public IList<string> Tools { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether every agent profile is exposed to MCP clients as a callable
    /// tool. When <c>false</c> (the default), no agent is exposed unless it is named in <see cref="Agents"/>.
    /// </summary>
    /// <remarks>
    /// Agents are gated separately from <see cref="ExposeAllTools"/> on purpose. An agent runs a whole AI
    /// profile — its system message, its tools, its data sources, and the credentials behind them — so
    /// letting the tool switch turn agents on would silently widen an existing deployment's surface the
    /// moment it upgraded.
    /// </remarks>
    public bool ExposeAllAgents { get; set; }

    /// <summary>
    /// Gets or sets the allow-list of agent profile names exposed to MCP clients when
    /// <see cref="ExposeAllAgents"/> is <c>false</c>. Each entry matches the <c>Name</c> of an AI profile of
    /// type agent.
    /// </summary>
    /// <remarks>
    /// This list is a security boundary: an allow-listed agent is runnable by any client that clears the
    /// server's authentication. It governs only what a client may <em>invoke</em> — the tools an agent uses
    /// internally to do its job are its own configuration and are never filtered by
    /// <see cref="Tools"/>, nor made directly callable by being listed here.
    /// </remarks>
    public IList<string> Agents { get; set; } = [];
}
