using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.AI.Documents.Ingestion;

/// <summary>
/// Picks the reader that can turn a piece of content into an <see cref="IngestionDocument"/>.
/// </summary>
public interface IIngestionDocumentReaderResolver
{
    /// <summary>
    /// Resolves the reader for the supplied content.
    /// </summary>
    /// <param name="fileName">The file name, including its extension.</param>
    /// <param name="mediaType">The media type of the content.</param>
    /// <param name="content">The content itself, when the resolver is allowed to inspect it.</param>
    /// <returns>The reader, or <see langword="null"/> when nothing registered can read the content.</returns>
    IngestionDocumentReader Resolve(string fileName, string mediaType, Stream content = null);
}
