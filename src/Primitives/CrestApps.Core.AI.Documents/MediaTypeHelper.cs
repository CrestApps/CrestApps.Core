namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Provides media type inference from file extensions for <see cref="Microsoft.Extensions.DataIngestion.IngestionDocumentReader"/> dispatch.
/// </summary>
public static class MediaTypeHelper
{
    private static readonly HashSet<string> _visionImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".gif", ".jpeg", ".jpg", ".png", ".webp"
    };

    /// <summary>
    /// Gets the supported vision image extensions.
    /// </summary>
    public static IReadOnlyCollection<string> VisionImageExtensions => _visionImageExtensions;

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
        return _visionImageExtensions.Contains(extension ?? string.Empty);
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

    /// <summary>
    /// Validates that the initial bytes of a stream match a known image file signature
    /// for the supported vision formats (PNG, JPEG, GIF, WebP, BMP).
    /// </summary>
    /// <param name="stream">A readable and seekable stream positioned at the start of the file.</param>
    /// <returns><see langword="true"/> if the file header matches a supported image format; otherwise, <see langword="false"/>.</returns>
    public static bool HasValidImageSignature(Stream stream)
    {
        if (stream == null || !stream.CanRead)
        {
            return false;
        }

        const int maxHeaderSize = 12;
        Span<byte> header = stackalloc byte[maxHeaderSize];

        var startPosition = stream.CanSeek ? stream.Position : 0;
        var bytesRead = stream.Read(header);

        if (stream.CanSeek)
        {
            stream.Position = startPosition;
        }

        if (bytesRead < 4)
        {
            return false;
        }

        // PNG: 89 50 4E 47
        if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
        {
            return true;
        }

        // JPEG: FF D8 FF
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return true;
        }

        // GIF: 47 49 46 38
        if (header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38)
        {
            return true;
        }

        // BMP: 42 4D
        if (header[0] == 0x42 && header[1] == 0x4D)
        {
            return true;
        }

        // WebP: RIFF....WEBP (bytes 0-3 = "RIFF", bytes 8-11 = "WEBP")
        if (bytesRead >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            return true;
        }

        return false;
    }
}
