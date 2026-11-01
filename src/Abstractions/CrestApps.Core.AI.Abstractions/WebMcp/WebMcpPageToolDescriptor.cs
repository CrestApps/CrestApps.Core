namespace CrestApps.Core.AI.WebMcp;

/// <summary>
/// A page tool as a browser tab reports it, before it is checked. The browser is not trusted, so every value is
/// validated before the tool reaches a model.
/// </summary>
public sealed class WebMcpPageToolDescriptor
{
    /// <summary>
    /// Gets or sets the function name of the tool.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the human-readable title of the tool.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets what the tool does, for the model.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the JSON schema of the tool's input, as JSON text.
    /// </summary>
    public string InputSchema { get; set; }
}
