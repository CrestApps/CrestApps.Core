namespace CrestApps.Core.AI.Documents.Generation;

/// <summary>
/// Resolves the <see cref="IGeneratedFileWriter"/> registered for a file extension and lists the
/// output formats that the current application supports.
/// </summary>
public interface IGeneratedFileWriterResolver
{
    /// <summary>
    /// Gets the supported output file extensions (each including a leading dot, e.g. <c>.pdf</c>).
    /// </summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <summary>
    /// Determines whether a writer is registered for the supplied extension.
    /// </summary>
    /// <param name="extension">The file extension, with or without a leading dot.</param>
    bool IsSupported(string extension);

    /// <summary>
    /// Resolves the writer registered for the supplied extension.
    /// </summary>
    /// <param name="extension">The file extension, with or without a leading dot.</param>
    /// <param name="writer">When this method returns, contains the resolved writer when one exists.</param>
    /// <returns><see langword="true"/> when a writer was resolved; otherwise, <see langword="false"/>.</returns>
    bool TryResolve(string extension, out IGeneratedFileWriter writer);
}
