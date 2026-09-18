using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Documents.Services;

/// <summary>
/// Processes uploaded files into AI documents and embedded chunks.
/// </summary>
public interface IAIDocumentProcessingService
{
    /// <summary>
    /// Processes an uploaded file by extracting text, chunking, generating embeddings, and creating an AI document.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <param name="referenceId">The reference id.</param>
    /// <param name="referenceType">The reference type.</param>
    /// <param name="embeddingGenerator">The embedding generator.</param>
    /// <param name="maxIndexableCharacters">
    /// How much extracted text this document may hold and still be indexed, or <see langword="null"/> to use
    /// the site's own limit. A caller that knows which AI profile the upload belongs to passes that profile's
    /// limit, so a profile handling large documents can raise it without changing what other profiles pay.
    /// </param>
    Task<DocumentProcessingResult> ProcessFileAsync(
        IFormFile file,
        string referenceId,
        string referenceType,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        int? maxIndexableCharacters = null,
        bool? analyzeImagesAtUpload = null);
}
