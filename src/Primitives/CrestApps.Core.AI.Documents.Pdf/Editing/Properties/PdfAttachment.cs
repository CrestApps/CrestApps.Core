using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// A file embedded in a PDF: listed in the document's embedded-files name tree, or attached to a page by a
/// file attachment annotation.
/// </summary>
internal sealed class PdfAttachment
{
    /// <summary>
    /// Gets or sets the name the attachment is listed under.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the attachment's file name.
    /// </summary>
    public string FileName { get; set; }

    /// <summary>
    /// Gets or sets the attachment's description.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the size of the file, in bytes, when the PDF records it or the stream holds it
    /// uncompressed.
    /// </summary>
    public long? Size { get; set; }

    /// <summary>
    /// Gets or sets the media type the PDF records for the file.
    /// </summary>
    public string MediaType { get; set; }

    /// <summary>
    /// Gets or sets the one-based page of a file attachment annotation, or <see langword="null"/> for a file
    /// listed in the name tree.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Gets or sets the file specification dictionary.
    /// </summary>
    public PdfDictionary FileSpecification { get; set; }

    /// <summary>
    /// Gets or sets the embedded file stream, or <see langword="null"/> when the specification only names
    /// an external file.
    /// </summary>
    public PdfDictionary Stream { get; set; }

    /// <summary>
    /// Returns whether the attachment answers to a name, by its listed name or its file name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when it matches, ignoring case.</returns>
    public bool Matches(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();

        return string.Equals(Name, trimmed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(FileName, trimmed, StringComparison.OrdinalIgnoreCase);
    }
}
