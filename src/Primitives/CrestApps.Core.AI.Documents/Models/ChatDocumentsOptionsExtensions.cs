namespace CrestApps.Core.AI.Documents.Models;

/// <summary>
/// Provides extension methods for chat Documents Options.
/// </summary>
public static class ChatDocumentsOptionsExtensions
{
    /// <summary>
    /// Gets allowed file extensions accept value.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="includeVisionImages">Whether to include supported vision image extensions.</param>
    /// <param name="includeDocuments">Whether to include document extensions.</param>
    public static string GetAllowedFileExtensionsAcceptValue(this ChatDocumentsOptions options, bool includeVisionImages = false, bool includeDocuments = true)
    {
        return BuildAcceptValue(GetAllowedFileExtensions(options, includeVisionImages, includeDocuments));
    }

    /// <summary>
    /// Gets allowed file extensions display value.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="includeVisionImages">Whether to include supported vision image extensions.</param>
    /// <param name="includeDocuments">Whether to include document extensions.</param>
    public static string GetAllowedFileExtensionsDisplayValue(this ChatDocumentsOptions options, bool includeVisionImages = false, bool includeDocuments = true)
    {
        return BuildDisplayValue(GetAllowedFileExtensions(options, includeVisionImages, includeDocuments));
    }

    /// <summary>
    /// Gets embeddable file extensions accept value.
    /// </summary>
    /// <param name="options">The options.</param>
    public static string GetEmbeddableFileExtensionsAcceptValue(this ChatDocumentsOptions options)
    {
        return BuildAcceptValue(options?.EmbeddableFileExtensions);
    }

    /// <summary>
    /// Gets embeddable file extensions display value.
    /// </summary>
    /// <param name="options">The options.</param>
    public static string GetEmbeddableFileExtensionsDisplayValue(this ChatDocumentsOptions options)
    {
        return BuildDisplayValue(options?.EmbeddableFileExtensions);
    }

    /// <summary>
    /// Gets the allowed file extensions.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="includeVisionImages">Whether to include supported vision image extensions.</param>
    /// <param name="includeDocuments">Whether to include document extensions.</param>
    public static IReadOnlyList<string> GetAllowedFileExtensions(this ChatDocumentsOptions options, bool includeVisionImages = false, bool includeDocuments = true)
    {
        IEnumerable<string> extensions = [];

        if (includeDocuments)
        {
            extensions = OrderExtensions(options?.AllowedFileExtensions);
        }

        if (includeVisionImages)
        {
            extensions = extensions.Concat(MediaTypeHelper.VisionImageExtensions);
        }

        return OrderExtensions(extensions);
    }

    /// <summary>
    /// Determines whether the extension is allowed.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="extension">The file extension.</param>
    /// <param name="includeVisionImages">Whether to include supported vision image extensions.</param>
    /// <param name="includeDocuments">Whether to include document extensions.</param>
    public static bool IsAllowedFileExtension(this ChatDocumentsOptions options, string extension, bool includeVisionImages = false, bool includeDocuments = true)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        return GetAllowedFileExtensions(options, includeVisionImages, includeDocuments)
            .Contains(extension.StartsWith('.') ? extension : '.' + extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether the supplied file (or extension) is a tabular file — an allowed document
    /// extension that is not embeddable (for example CSV or XLSX). Tabular files are loaded into the
    /// in-memory tabular workspace and queried with SQL instead of being vector-indexed.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="fileNameOrExtension">The file name or extension to test.</param>
    public static bool IsTabularFileExtension(this ChatDocumentsOptions options, string fileNameOrExtension)
    {
        if (options is null || string.IsNullOrWhiteSpace(fileNameOrExtension))
        {
            return false;
        }

        var extension = System.IO.Path.GetExtension(fileNameOrExtension);

        if (string.IsNullOrEmpty(extension))
        {
            extension = fileNameOrExtension.StartsWith('.') ? fileNameOrExtension : '.' + fileNameOrExtension;
        }

        return options.AllowedFileExtensions.Contains(extension)
            && !options.EmbeddableFileExtensions.Contains(extension);
    }

    private static string BuildAcceptValue(IEnumerable<string> extensions)
    {
        return string.Join(',', OrderExtensions(extensions));
    }

    private static string BuildDisplayValue(IEnumerable<string> extensions)
    {
        return string.Join(", ", OrderExtensions(extensions));
    }

    private static string[] OrderExtensions(IEnumerable<string> extensions)
    {
        return (extensions ?? []).Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
