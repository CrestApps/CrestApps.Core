namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// How a text string is stored in a PDF file.
/// </summary>
internal enum PdfStringEncoding
{
    /// <summary>
    /// One byte per character in PDFDocEncoding when every character has one, and UTF-16 otherwise.
    /// </summary>
    Automatic,

    /// <summary>
    /// Always UTF-16, big-endian, after a byte order mark.
    /// </summary>
    Unicode,
}
