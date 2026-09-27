using CrestApps.Core.AI.Documents.Pdf.Composition;

namespace CrestApps.Core.AI.Documents.Pdf.Workspace;

/// <summary>
/// A PDF the agent is working on in a conversation: either a document it is composing, or a file produced
/// by editing an upload.
/// </summary>
/// <remarks>
/// Uploads are never changed. The first edit of an upload writes a working copy, and every later edit is made
/// to that copy, so the original can always be gone back to and the reader always knows which file an answer
/// is about.
/// </remarks>
internal sealed class PdfWorkingDocument
{
    /// <summary>
    /// The kind of a document being composed from a definition.
    /// </summary>
    public const string ComposedKind = "composed";

    /// <summary>
    /// The kind of a document held as a finished file.
    /// </summary>
    public const string FileKind = "file";

    /// <summary>
    /// Gets or sets the name the document is addressed by.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the kind: <see cref="ComposedKind"/> or <see cref="FileKind"/>.
    /// </summary>
    public string Kind { get; set; }

    /// <summary>
    /// Gets or sets the definition of a composed document.
    /// </summary>
    public PdfDocumentDefinition Definition { get; set; }

    /// <summary>
    /// Gets or sets where the bytes of a file document are stored.
    /// </summary>
    public string BlobPath { get; set; }

    /// <summary>
    /// Gets or sets the size of a file document, in bytes.
    /// </summary>
    public long ByteLength { get; set; }

    /// <summary>
    /// Gets or sets the number of pages the document had when it was last saved or rendered.
    /// </summary>
    public int PageCount { get; set; }

    /// <summary>
    /// Gets or sets the file name the document was derived from, when it came from an upload.
    /// </summary>
    public string SourceFileName { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the upload the document was derived from.
    /// </summary>
    public string SourceDocumentId { get; set; }

    /// <summary>
    /// Gets or sets what has been done to the document, oldest first.
    /// </summary>
    public List<string> History { get; set; } = [];

    /// <summary>
    /// Gets or sets the number of times the document has been changed.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets when the document was last changed.
    /// </summary>
    public DateTime UpdatedUtc { get; set; }

    /// <summary>
    /// Gets a value indicating whether the document is composed from a definition.
    /// </summary>
    public bool IsComposed => string.Equals(Kind, ComposedKind, StringComparison.Ordinal);
}
