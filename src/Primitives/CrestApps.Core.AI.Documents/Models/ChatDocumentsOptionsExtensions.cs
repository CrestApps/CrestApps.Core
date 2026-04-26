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
    public static string GetAllowedFileExtensionsAcceptValue(this ChatDocumentsOptions options)
    {
        return BuildAcceptValue(options?.AllowedFileExtensions);
    }

    /// <summary>
    /// Gets allowed file extensions display value.
    /// </summary>
    /// <param name="options">The options.</param>
    public static string GetAllowedFileExtensionsDisplayValue(this ChatDocumentsOptions options)
    {
        return BuildDisplayValue(options?.AllowedFileExtensions);
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
