namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// The document information an XMP packet mirrors.
/// </summary>
internal sealed class PdfXmpValues
{
    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the author.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets the subject.
    /// </summary>
    public string Subject { get; set; }

    /// <summary>
    /// Gets or sets the keywords.
    /// </summary>
    public string Keywords { get; set; }

    /// <summary>
    /// Gets or sets the application that created the original document.
    /// </summary>
    public string Creator { get; set; }

    /// <summary>
    /// Gets or sets the application that wrote the file.
    /// </summary>
    public string Producer { get; set; }

    /// <summary>
    /// Gets or sets the creation date, as a PDF date.
    /// </summary>
    public string CreationDate { get; set; }

    /// <summary>
    /// Gets or sets the modification date, as a PDF date.
    /// </summary>
    public string ModificationDate { get; set; }

    /// <summary>
    /// Gets or sets the document's language, as a BCP 47 tag.
    /// </summary>
    public string Language { get; set; }
}
