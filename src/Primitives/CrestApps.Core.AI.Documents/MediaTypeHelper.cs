namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Provides media type inference from file extensions for <see cref="Microsoft.Extensions.DataIngestion.IngestionDocumentReader"/> dispatch.
/// </summary>
public static class MediaTypeHelper
{
    /// <summary>
    /// Infers the media type for a file extension, falling back to the provided content type
    /// or <c>text/plain</c> if no mapping is found.
    /// </summary>
    /// <param name="extension">The file extension (e.g., <c>.pdf</c>).</param>
    /// <param name="fallbackContentType">An optional fallback content type from the HTTP request.</param>
    /// <returns>The inferred media type string.</returns>
    public static string InferMediaType(string extension, string fallbackContentType = null)
    {
        var mediaType = extension?.ToLowerInvariant() switch
        {
            ".bmp" => "image/bmp",
            ".md" => "text/markdown",
            ".gif" => "image/gif",
            ".jpeg" or ".jpg" => "image/jpeg",
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".html" or ".htm" => "text/html",
            ".json" => "application/json",
            ".webp" => "image/webp",
            ".xml" => "application/xml",
            ".csv" => "text/csv",
            ".yaml" or ".yml" => "text/yaml",
            _ => null,
        };

        return mediaType ?? fallbackContentType ?? "text/plain";
    }

    /// <summary>
    /// Determines whether the specified extension is a supported vision image format.
    /// </summary>
    /// <param name="extension">The file extension.</param>
    public static bool IsVisionImageExtension(string extension)
    {
        return extension?.ToLowerInvariant() switch
        {
            ".bmp" or ".gif" or ".jpeg" or ".jpg" or ".png" or ".webp" => true,
            _ => false,
        };
    }

    /// <summary>
    /// Determines whether the specified media type represents a supported vision image format.
    /// </summary>
    /// <param name="mediaType">The media type.</param>
    public static bool IsVisionImageMediaType(string mediaType)
    {
        return mediaType?.ToLowerInvariant() switch
        {
            "image/bmp" or "image/gif" or "image/jpeg" or "image/png" or "image/webp" => true,
            _ => false,
        };
    }
}
