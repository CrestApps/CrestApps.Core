namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// An annotation found on a page.
/// </summary>
internal sealed class PdfAnnotationInfo
{
    /// <summary>
    /// Gets or sets the id updates and removals name it by.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the one-based page.
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Gets or sets the kind, in the words the tool uses: <c>highlight</c>, <c>note</c>, <c>link</c>, ….
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the position, in points from the top-left of the page.
    /// </summary>
    public (double X, double Y, double Width, double Height) Position { get; set; }

    /// <summary>
    /// Gets or sets the comment text.
    /// </summary>
    public string Contents { get; set; }

    /// <summary>
    /// Gets or sets the author.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets the subject.
    /// </summary>
    public string Subject { get; set; }

    /// <summary>
    /// Gets or sets the colour, as hexadecimal.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets when it was last changed, as the file records it.
    /// </summary>
    public string Modified { get; set; }

    /// <summary>
    /// Gets or sets the text a highlight, underline or strikeout marks.
    /// </summary>
    public string MarkedText { get; set; }

    /// <summary>
    /// Gets or sets where a link goes.
    /// </summary>
    public string Target { get; set; }

    /// <summary>
    /// Gets or sets the number of replies to it.
    /// </summary>
    public int Replies { get; set; }
}
