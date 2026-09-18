using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Ingestion;

/// <summary>
/// Picks a reader by what the content actually is, and only then by what it is called.
/// </summary>
/// <remarks>
/// An extension is a claim, not a fact. A connector that fetches over HTTP knows the media type the server
/// declared and often has no filename at all; a file dropped into a watched folder may be a PDF called
/// <c>.txt</c>. Asking the declared type first, then the bytes, then the name, gets the right reader in every
/// one of those cases and keeps today's behaviour for a plain upload.
/// </remarks>
public sealed class DefaultIngestionDocumentReaderResolver : IIngestionDocumentReaderResolver
{
    /// <summary>
    /// The media type a caller uses when it does not know. It never identifies anything, so it is treated as
    /// though nothing was declared.
    /// </summary>
    private const string UnknownMediaType = "application/octet-stream";

    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultIngestionDocumentReaderResolver"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider the keyed readers are registered on.</param>
    public DefaultIngestionDocumentReaderResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Resolves the reader for the supplied content.
    /// </summary>
    /// <param name="fileName">The file name, including its extension.</param>
    /// <param name="mediaType">The media type the caller declared, when it knows one.</param>
    /// <param name="content">The content, inspected only when the declared type says nothing.</param>
    /// <returns>The reader, or <see langword="null"/> when nothing registered can read the content.</returns>
    public IngestionDocumentReader Resolve(string fileName, string mediaType, Stream content = null)
    {
        var declared = Normalize(mediaType);

        if (declared is not null)
        {
            var reader = _serviceProvider.GetKeyedService<IngestionDocumentReader>(declared);

            if (reader is not null)
            {
                return reader;
            }
        }

        var extension = Path.GetExtension(fileName);
        var sniffed = Sniff(content, extension);

        if (sniffed is not null && !string.Equals(sniffed, declared, StringComparison.OrdinalIgnoreCase))
        {
            var reader = _serviceProvider.GetKeyedService<IngestionDocumentReader>(sniffed);

            if (reader is not null)
            {
                return reader;
            }
        }

        if (string.IsNullOrEmpty(extension))
        {
            return null;
        }

        return _serviceProvider.GetKeyedService<IngestionDocumentReader>(extension);
    }

    private static string Normalize(string mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return null;
        }

        // "text/plain; charset=utf-8" is a declaration of text/plain with a detail attached.
        var separator = mediaType.IndexOf(';', StringComparison.Ordinal);
        var value = (separator < 0 ? mediaType : mediaType[..separator]).Trim();

        if (value.Length == 0 || string.Equals(value, UnknownMediaType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value;
    }

    /// <summary>
    /// Reads the first few bytes and says what they are.
    /// </summary>
    /// <param name="content">The content. A stream that cannot seek is left alone.</param>
    /// <param name="extension">The file extension, used only to tell one zip container from another.</param>
    /// <returns>The media type, or <see langword="null"/> when the bytes say nothing.</returns>
    /// <remarks>
    /// The stream is always returned to where it was found. A reader that receives a stream three bytes in
    /// fails in a way that looks like a corrupt file.
    /// </remarks>
    private static string Sniff(Stream content, string extension)
    {
        if (content is null || !content.CanSeek || !content.CanRead || content.Length < 4)
        {
            return null;
        }

        var position = content.Position;

        try
        {
            Span<byte> header = stackalloc byte[8];

            content.Position = 0;

            var read = content.Read(header);

            if (read < 4)
            {
                return null;
            }

            if (header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46)
            {
                return "application/pdf";
            }

            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            {
                return "image/png";
            }

            if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                return "image/jpeg";
            }

            // A zip container is a .docx, .xlsx, .pptx or an actual archive, and only the name says which.
            if (header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04)
            {
                return extension?.ToLowerInvariant() switch
                {
                    ".docx" or ".xlsx" or ".pptx" => MediaTypeHelper.InferMediaType(extension),
                    _ => null,
                };
            }

            return null;
        }
        catch (IOException)
        {
            // A stream that cannot be read here is a stream the reader will fail on anyway, and failing
            // there says something useful about the file.
            return null;
        }
        finally
        {
            content.Position = position;
        }
    }
}
