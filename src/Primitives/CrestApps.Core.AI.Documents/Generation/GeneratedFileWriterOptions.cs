namespace CrestApps.Core.AI.Documents.Generation;

/// <summary>
/// Tracks the file extensions for which an <see cref="IGeneratedFileWriter"/> has been registered.
/// Used to advertise the supported output formats and to validate requested formats before writing.
/// </summary>
public sealed class GeneratedFileWriterOptions
{
    private readonly HashSet<string> _extensions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the registered, normalized file extensions (each including a leading dot, e.g. <c>.pdf</c>).
    /// </summary>
    public IReadOnlyCollection<string> Extensions => _extensions;

    /// <summary>
    /// Registers a supported file extension.
    /// </summary>
    /// <param name="extension">The file extension, with or without a leading dot.</param>
    public void Add(string extension)
    {
        var normalized = Normalize(extension);

        if (!string.IsNullOrEmpty(normalized))
        {
            _extensions.Add(normalized);
        }
    }

    /// <summary>
    /// Normalizes a file extension to a lower-case value that always starts with a dot.
    /// </summary>
    /// <param name="extension">The file extension to normalize.</param>
    /// <returns>The normalized extension, or an empty string when <paramref name="extension"/> is blank.</returns>
    public static string Normalize(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var trimmed = extension.Trim().ToLowerInvariant();

        return trimmed.StartsWith('.')
            ? trimmed
            : '.' + trimmed;
    }
}
