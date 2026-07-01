namespace CrestApps.Core.AI.Documents.Generation;

/// <summary>
/// Describes a request to materialize generated content as a downloadable AI document scoped to the
/// active conversation (chat session or chat interaction).
/// </summary>
public sealed class GeneratedDocumentRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeneratedDocumentRequest"/> class.
    /// </summary>
    /// <param name="referenceId">The owning conversation identifier the document is attached to.</param>
    /// <param name="referenceType">The owning conversation reference type.</param>
    /// <param name="fileName">The requested file name, including the format extension.</param>
    /// <param name="content">The content to write to the generated file.</param>
    public GeneratedDocumentRequest(
        string referenceId,
        string referenceType,
        string fileName,
        GeneratedFileContent content)
    {
        ArgumentException.ThrowIfNullOrEmpty(referenceId);
        ArgumentException.ThrowIfNullOrEmpty(referenceType);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(content);

        ReferenceId = referenceId;
        ReferenceType = referenceType;
        FileName = fileName;
        Content = content;
    }

    /// <summary>
    /// Gets the owning conversation identifier the document is attached to.
    /// </summary>
    public string ReferenceId { get; }

    /// <summary>
    /// Gets the owning conversation reference type.
    /// </summary>
    public string ReferenceType { get; }

    /// <summary>
    /// Gets the requested file name, including the format extension.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Gets the content to write to the generated file.
    /// </summary>
    public GeneratedFileContent Content { get; }
}
