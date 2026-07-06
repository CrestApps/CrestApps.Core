using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Generation;

/// <summary>
/// Represents the outcome of creating a downloadable generated document.
/// </summary>
public sealed class GeneratedDocumentResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeneratedDocumentResult"/> class.
    /// </summary>
    /// <param name="document">The persisted generated document.</param>
    /// <param name="referenceToken">The download reference marker to include in the model response, when available.</param>
    public GeneratedDocumentResult(AIDocument document, string referenceToken)
    {
        Document = document;
        ReferenceToken = referenceToken;
    }

    /// <summary>
    /// Gets the persisted generated document.
    /// </summary>
    public AIDocument Document { get; }

    /// <summary>
    /// Gets the download reference marker (for example <c>[doc:1]</c>) that the model must include
    /// verbatim in its response so the UI renders a download link, or <see langword="null"/> when no
    /// active invocation scope is available to register the reference.
    /// </summary>
    public string ReferenceToken { get; }
}
