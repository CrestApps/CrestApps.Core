namespace CrestApps.Core.AI.Documents.Generation.RichText;

/// <summary>
/// A run of text within a block that shares one set of inline formatting.
/// </summary>
public sealed class RichTextSpan
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RichTextSpan"/> class.
    /// </summary>
    /// <param name="text">The literal text.</param>
    public RichTextSpan(string text)
    {
        Text = text ?? string.Empty;
    }

    /// <summary>
    /// Gets the literal text, with any markup that produced the formatting already removed.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the run is bold.
    /// </summary>
    public bool Bold { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the run is italic.
    /// </summary>
    public bool Italic { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the run is struck through.
    /// </summary>
    public bool Strikethrough { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the run is inline code.
    /// </summary>
    public bool Code { get; set; }

    /// <summary>
    /// Gets or sets the target of a link, when the run is a link.
    /// </summary>
    public string Link { get; set; }
}
