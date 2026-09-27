namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// A table of contents built from the document's headings. Each entry links to its heading and carries the
/// page number the heading was laid out on.
/// </summary>
internal sealed class PdfTableOfContentsDefinition
{
    /// <summary>
    /// Gets or sets a value indicating whether the table of contents is printed.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Gets or sets the title printed above the entries.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the deepest heading level listed.
    /// </summary>
    public int? Depth { get; set; }
}
