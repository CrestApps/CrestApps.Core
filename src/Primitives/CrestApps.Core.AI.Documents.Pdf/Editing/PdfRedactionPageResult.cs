namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// What a redaction removed from one page.
/// </summary>
internal sealed class PdfRedactionPageResult
{
    /// <summary>
    /// Gets or sets the one-based page number.
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Gets or sets how many areas were redacted.
    /// </summary>
    public int Areas { get; set; }

    /// <summary>
    /// Gets or sets how many glyphs were removed from the content.
    /// </summary>
    public int Glyphs { get; set; }

    /// <summary>
    /// Gets or sets how many images were removed.
    /// </summary>
    public int Images { get; set; }

    /// <summary>
    /// Gets or sets how many annotations and form widgets were removed.
    /// </summary>
    public int Annotations { get; set; }

    /// <summary>
    /// Gets or sets how many characters a reader can still extract inside the areas after the redaction.
    /// </summary>
    public int Remaining { get; set; }

    /// <summary>
    /// Gets or sets why the content could not be rewritten, when it could not.
    /// </summary>
    public string Error { get; set; }
}
