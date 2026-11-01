namespace CrestApps.Core.AI.WebMcp;

/// <summary>
/// A page tool as a browser tab reports it, before it is checked. The browser is not trusted, so every value is
/// validated before the tool reaches a model.
/// </summary>
public sealed class WebMcpPageToolDescriptor
{
    /// <summary>
    /// Gets or sets the WebMCP name of the tool, as the page registered it with <c>document.modelContext.registerTool</c>.
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

    /// <summary>
    /// Gets or sets a value indicating whether the tool declared the WebMCP <c>readOnlyHint</c> annotation: it only reads.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tool declared the WebMCP <c>consequentialHint</c> annotation: running
    /// it has significant, real-world or irreversible effects, such as placing an order.
    /// </summary>
    public bool Consequential { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tool declared the WebMCP <c>untrustedContentHint</c> annotation: what it
    /// answers holds content the page did not write.
    /// </summary>
    public bool UntrustedContent { get; set; }
}
