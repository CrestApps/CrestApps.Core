using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents a tool entry in the unified tool registry.
/// Contains searchable metadata used for tool scoping during orchestration.
/// </summary>
public sealed class ToolRegistryEntry
{
    /// <summary>
    /// Gets or sets the unique identifier for this entry.
    /// Used for internal retrieval and to avoid name collisions across sources.
    /// For local/system tools this equals <see cref="Name"/>;
    /// for MCP tools it includes the source identifier (e.g., <c>mcp:{connectionId}:{toolName}</c>).
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the function name of the tool as presented to the AI model.
    /// For local tools, this is the registered tool name.
    /// For MCP tools, this is the tool name as reported by the MCP server.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets a human-readable description of the tool's capabilities.
    /// Used for relevance matching during tool scoping.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the source type of this tool entry.
    /// </summary>
    public ToolRegistryEntrySource Source { get; set; }

    /// <summary>
    /// Gets or sets the source identifier.
    /// For MCP tools, this is the MCP connection ID.
    /// For local tools, this is <see langword="null"/>.
    /// </summary>
    public string SourceId { get; set; }

    /// <summary>
    /// Gets or sets a factory delegate that creates the actual <see cref="AITool"/> for this entry.
    /// Each provider sets this to its own resolution logic (e.g., DI lookup for local tools,
    /// MCP proxy creation for MCP tools).
    /// </summary>
    public Func<IServiceProvider, ValueTask<AITool>> CreateAsync { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a person must approve each call of this tool before it runs. A caller
    /// that can ask for an approval, such as an AG-UI chat run, pauses for one before the call. Any other caller never
    /// runs the tool: it gets a stand-in with the same name and schema that does not run it and tells the model why
    /// (a tool that is not an <see cref="AIFunction"/> is left out instead).
    /// </summary>
    public bool RequiresApproval { get; set; }

    /// <summary>
    /// Gets or sets the least strict approval the tool's own registration allows, or <see langword="null"/> when
    /// it has none. A tool registered with an explicit <c>.RequireApproval()</c>, and a consequential WebMCP page
    /// tool, have <see cref="ToolApprovalMode.EveryCall"/>: a profile's approval policy never lets such a tool run
    /// without asking. A tool that requires an approval only because of its <see cref="Kind"/> has none.
    /// </summary>
    public ToolApprovalMode? ApprovalFloor { get; set; }

    /// <summary>
    /// Gets or sets what the tool does to the application's data and to the outside world, when its registration
    /// says so. Tools from sources that carry no kind, such as MCP servers and agents, keep
    /// <see cref="AIToolKind.Unspecified"/>.
    /// </summary>
    public AIToolKind Kind { get; set; }
}
