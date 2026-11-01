namespace CrestApps.Core.AI.WebMcp.Models;

/// <summary>
/// Controls which AI tools browser agents may call through the WebMCP tool endpoints.
/// </summary>
public sealed class WebMcpOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether every selectable tool and tool instance is given to browser agents.
    /// When <see langword="false"/> (the default), only the tools in <see cref="Tools"/> are given. System tools and
    /// hidden tools are never given.
    /// </summary>
    public bool ExposeAllTools { get; set; }

    /// <summary>
    /// Gets or sets the names of the tools and tool instances given to browser agents when
    /// <see cref="ExposeAllTools"/> is <see langword="false"/>. Names are matched without regard to case.
    /// </summary>
    public IList<string> Tools { get; set; } = [];
}
