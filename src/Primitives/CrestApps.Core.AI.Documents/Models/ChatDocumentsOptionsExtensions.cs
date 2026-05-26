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
    public static string GetAllowedFileExtensionsAcceptValue(this ChatDocumentsOptions options, bool includeVisionImages = false)
    {
        return BuildAcceptValue(GetAllowedFileExtensions(options, includeVisionImages));
    }

    /// <summary>
    /// Gets allowed file extensions display value.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="includeVisionImages">Whether to include supported vision image extensions.</param>
    public static string GetAllowedFileExtensionsDisplayValue(this ChatDocumentsOptions options, bool includeVisionImages = false)
    {
        return BuildDisplayValue(GetAllowedFileExtensions(options, includeVisionImages));
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
    public static IReadOnlyList<string> GetAllowedFileExtensions(this ChatDocumentsOptions options, bool includeVisionImages = false)
    {
        var extensions = OrderExtensions(options?.AllowedFileExtensions);

        return includeVisionImages
            ? OrderExtensions(extensions.Concat(MediaTypeHelper.VisionImageExtensions))
            : extensions;
    }

    /// <summary>
    /// Determines whether the extension is allowed.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="extension">The file extension.</param>
    /// <param name="includeVisionImages">Whether to include supported vision image extensions.</param>
    public static bool IsAllowedFileExtension(this ChatDocumentsOptions options, string extension, bool includeVisionImages = false)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        return GetAllowedFileExtensions(options, includeVisionImages)
            .Contains(extension.StartsWith('.') ? extension : '.' + extension, StringComparer.OrdinalIgnoreCase);
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
