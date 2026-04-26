using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Services;

/// <summary>
/// Represents the result of processing an uploaded AI document.
/// </summary>
public sealed class DocumentProcessingResult
{
    /// <summary>
    /// Gets or sets the success.
    /// </summary>
    public bool Success { get; private set; }

    /// <summary>
    /// Gets or sets the document.
    /// </summary>
    public AIDocument Document { get; private set; }

    /// <summary>
    /// Gets or sets the document Info.
    /// </summary>
    public ChatDocumentInfo DocumentInfo { get; private set; }

    /// <summary>
    /// Gets or sets the chunks.
    /// </summary>
    public IReadOnlyList<AIDocumentChunk> Chunks { get; private set; }

    /// <summary>
    /// Gets or sets the error.
    /// </summary>
    public string Error { get; private set; }

    /// <summary>
    /// Succeededs the operation.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="documentInfo">The document info.</param>
    /// <param name="chunks">The chunks.</param>
    public static DocumentProcessingResult Succeeded(AIDocument document, ChatDocumentInfo documentInfo, IReadOnlyList<AIDocumentChunk> chunks)
    {
        return new DocumentProcessingResult
        {
            Success = true,
            Document = document,
            DocumentInfo = documentInfo,
            Chunks = chunks ?? [],
        };
    }

    /// <summary>
    /// Faileds the operation.
    /// </summary>
    /// <param name="error">The error.</param>
    public static DocumentProcessingResult Failed(string error)
    {
        return new DocumentProcessingResult
        {
            Success = false,
            Error = error,
            Chunks = [],
        };
    }
}
