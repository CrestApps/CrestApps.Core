namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Identifies a tabular document that participates in a workspace, mapping the source
/// document to the table it is loaded into.
/// </summary>
public sealed class TabularDocumentRef
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TabularDocumentRef"/> class.
    /// </summary>
    /// <param name="documentId">The source document identifier.</param>
    /// <param name="fileName">The original file name of the document.</param>
    public TabularDocumentRef(string documentId, string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId);

        DocumentId = documentId;
        FileName = fileName;
    }

    /// <summary>
    /// Gets the source document identifier.
    /// </summary>
    public string DocumentId { get; }

    /// <summary>
    /// Gets the original file name of the document.
    /// </summary>
    public string FileName { get; }
}
