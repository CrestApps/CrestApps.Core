using System.Text.Json;
using System.Text.Json.Serialization;
using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.AI.WebMcp.Models;

/// <summary>
/// Describes a tool a browser agent may call through WebMCP.
/// </summary>
public sealed class WebMcpToolDescriptor
{
    /// <summary>
    /// Gets or sets the function name the agent calls the tool by.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the human-readable title of the tool.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets what the tool does, for the agent.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the JSON schema of the tool's input.
    /// </summary>
    [JsonPropertyName("inputSchema")]
    public JsonElement InputSchema { get; set; }

    /// <summary>
    /// Gets or sets what the tool does to the application's data and to the outside world, when its registration
    /// says so: <c>read</c>, <c>write</c>, <c>destructive</c>, <c>external</c> or <c>security</c> (the names of
    /// <see cref="AIToolKind"/> in lower case). It is left out of the JSON when the tool has no kind.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Kind { get; set; }
}
