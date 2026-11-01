using System.Text.Json;

namespace CrestApps.Core.AI.WebMcp;

/// <summary>
/// A checked page tool a model may call in the browser tab that registered it.
/// </summary>
public sealed class WebMcpPageTool
{
    /// <summary>
    /// Gets the function name of the tool.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Gets the human-readable title of the tool.
    /// </summary>
    public string Title { get; init; }

    /// <summary>
    /// Gets what the tool does, for the model.
    /// </summary>
    public string Description { get; init; }

    /// <summary>
    /// Gets the JSON schema of the tool's input.
    /// </summary>
    public JsonElement InputSchema { get; init; }
}
