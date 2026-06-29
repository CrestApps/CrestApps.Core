namespace CrestApps.Core.AI.Documents.Generation;

/// <summary>
/// Writes <see cref="GeneratedFileContent"/> to a stream in a specific file format. Implementations are
/// registered per file extension and resolved through <see cref="IGeneratedFileWriterResolver"/> so that
/// generated files (chat downloads and tabular exports) can be produced in any supported format.
/// </summary>
public interface IGeneratedFileWriter
{
    /// <summary>
    /// Writes the supplied content to <paramref name="destination"/> in the writer's target format.
    /// </summary>
    /// <param name="content">The content to write.</param>
    /// <param name="destination">The destination stream that receives the encoded file bytes.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task WriteAsync(GeneratedFileContent content, Stream destination, CancellationToken cancellationToken = default);
}
