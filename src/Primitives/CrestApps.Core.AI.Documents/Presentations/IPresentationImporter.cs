namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// Turns an uploaded file into a deck the presentation workspace can edit. Implementations are registered as
/// keyed services under the file extension they read, such as <c>.pptx</c> or <c>.potx</c>.
/// </summary>
public interface IPresentationImporter
{
    /// <summary>
    /// Imports an uploaded file. The upload itself is never changed; the workspace edits the copy returned.
    /// </summary>
    /// <param name="source">The uploaded file.</param>
    /// <param name="fileName">The uploaded file's name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An editable presentation package, or <see langword="null"/> when the file cannot be read.</returns>
    Task<byte[]> ImportAsync(Stream source, string fileName, CancellationToken cancellationToken = default);
}
