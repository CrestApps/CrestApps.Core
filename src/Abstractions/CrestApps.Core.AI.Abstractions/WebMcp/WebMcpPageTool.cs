using System.Text.Json;

namespace CrestApps.Core.AI.WebMcp;

/// <summary>
/// A checked page tool a model may call in the browser tab that registered it.
/// </summary>
public sealed class WebMcpPageTool
{
    /// <summary>
    /// Gets the WebMCP name of the tool, which the browser tab runs it by.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Gets the function name the model calls the tool by. WebMCP names may hold dots and run to 128 characters, which AI
    /// providers refuse, so this is the name with those characters replaced and, when too long, shortened with a hash.
    /// </summary>
    public string FunctionName { get; init; }

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

    /// <summary>
    /// Gets a value indicating whether the tool declared the WebMCP <c>readOnlyHint</c> annotation.
    /// </summary>
    public bool ReadOnly { get; init; }

    /// <summary>
    /// Gets a value indicating whether the tool declared the WebMCP <c>consequentialHint</c> annotation.
    /// </summary>
    public bool Consequential { get; init; }

    /// <summary>
    /// Gets a value indicating whether the tool declared the WebMCP <c>untrustedContentHint</c> annotation.
    /// </summary>
    public bool UntrustedContent { get; init; }
}
