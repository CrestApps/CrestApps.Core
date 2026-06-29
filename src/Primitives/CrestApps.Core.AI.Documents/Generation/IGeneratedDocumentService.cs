namespace CrestApps.Core.AI.Documents.Generation;

/// <summary>
/// Creates downloadable AI documents from generated content. The produced file is written in the format
/// implied by the requested file name, stored through the document file store, persisted as a generated
/// <see cref="Models.AIDocument"/>, and surfaced to the chat UI through a download reference marker.
/// </summary>
public interface IGeneratedDocumentService
{
    /// <summary>
    /// Writes the requested content to a new generated document and registers its download reference.
    /// </summary>
    /// <param name="request">The generation request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created document and its download reference marker.</returns>
    Task<GeneratedDocumentResult> CreateAsync(GeneratedDocumentRequest request, CancellationToken cancellationToken = default);
}
