using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Ingestion;

/// <summary>
/// Covers which reader gets the bytes. An extension is a claim, not a fact: a connector that fetched over
/// HTTP knows the media type and often has no filename at all, and a file dropped into a watched folder may
/// be a PDF called .txt.
/// </summary>
public sealed class IngestionDocumentReaderResolverTests
{
    /// <summary>
    /// Verifies that a declared media type wins over a misleading extension.
    /// </summary>
    [Fact]
    public void Resolve_DeclaredPdfMediaType_WinsOverTxtExtension()
    {
        var resolver = CreateResolver(out var readers);

        var reader = resolver.Resolve("report.txt", "application/pdf");

        Assert.Same(readers["application/pdf"], reader);
    }

    /// <summary>
    /// Verifies that a detail attached to a media type does not stop it being recognized.
    /// </summary>
    [Fact]
    public void Resolve_MediaTypeWithCharset_IsRecognized()
    {
        var resolver = CreateResolver(out var readers);

        var reader = resolver.Resolve("page", "text/plain; charset=utf-8");

        Assert.Same(readers["text/plain"], reader);
    }

    /// <summary>
    /// Verifies that a caller who declares nothing useful gets the reader the bytes call for, and that the
    /// stream is handed on exactly where it was found.
    /// </summary>
    [Fact]
    public void Resolve_OctetStream_FallsBackToSniffingAndRewindsStream()
    {
        var resolver = CreateResolver(out var readers);

        using var content = new MemoryStream("%PDF-1.7\nrest of the file"u8.ToArray());

        content.Position = 3;

        var reader = resolver.Resolve("report.bin", "application/octet-stream", content);

        Assert.Same(readers["application/pdf"], reader);
        Assert.Equal(3, content.Position);
    }

    /// <summary>
    /// Verifies that a zip container is disambiguated by its name, because nothing in the bytes says which
    /// kind of document it holds.
    /// </summary>
    [Fact]
    public void Resolve_ZipWithDocxExtension_ReturnsOpenXmlReader()
    {
        var resolver = CreateResolver(out var readers);

        using var content = new MemoryStream([0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00]);

        var reader = resolver.Resolve("report.docx", null, content);

        Assert.Same(readers["application/vnd.openxmlformats-officedocument.wordprocessingml.document"], reader);
    }

    /// <summary>
    /// Verifies that a zip container with no telling extension is left to the extension registration rather
    /// than guessed at.
    /// </summary>
    [Fact]
    public void Resolve_ZipWithUnknownExtension_FallsBackToExtension()
    {
        var resolver = CreateResolver(out var readers);

        using var content = new MemoryStream([0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00]);

        var reader = resolver.Resolve("archive.txt", null, content);

        Assert.Same(readers[".txt"], reader);
    }

    /// <summary>
    /// Verifies that nothing known about the content still falls back to the extension, which is how every
    /// reader was resolved before any of this existed.
    /// </summary>
    [Fact]
    public void Resolve_Unknown_FallsBackToExtension()
    {
        var resolver = CreateResolver(out var readers);

        var reader = resolver.Resolve("notes.txt", null);

        Assert.Same(readers[".txt"], reader);
    }

    /// <summary>
    /// Verifies that nothing registered for the content means no reader, rather than an arbitrary one.
    /// </summary>
    [Fact]
    public void Resolve_NothingRegistered_ReturnsNull()
    {
        var resolver = CreateResolver(out _);

        Assert.Null(resolver.Resolve("model.step", "application/step"));
    }

    /// <summary>
    /// Verifies that registering a reader for a key a second time replaces the first, which is how a host
    /// that adds a better HTML reader takes over from the plain-text one.
    /// </summary>
    [Fact]
    public void Resolve_RegisteredTwice_LastWins()
    {
        var first = new StubReader("first");
        var second = new StubReader("second");

        var services = new ServiceCollection();

        services.AddKeyedSingleton<IngestionDocumentReader>("text/html", first);
        services.AddKeyedSingleton<IngestionDocumentReader>("text/html", second);

        using var provider = services.BuildServiceProvider();

        var resolver = new DefaultIngestionDocumentReaderResolver(provider);

        Assert.Same(second, resolver.Resolve("page.html", "text/html"));
    }

    private static DefaultIngestionDocumentReaderResolver CreateResolver(out Dictionary<string, IngestionDocumentReader> readers)
    {
        readers = new Dictionary<string, IngestionDocumentReader>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = new StubReader("pdf"),
            ["text/plain"] = new StubReader("text"),
            [".txt"] = new StubReader("txt-extension"),
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = new StubReader("docx"),
        };

        var services = new ServiceCollection();

        foreach (var (key, reader) in readers)
        {
            services.AddKeyedSingleton(key, reader);
        }

        return new DefaultIngestionDocumentReaderResolver(services.BuildServiceProvider());
    }

    private sealed class StubReader : IngestionDocumentReader
    {
        private readonly string _name;

        public StubReader(string name)
        {
            _name = name;
        }

        public override Task<IngestionDocument> ReadAsync(
            Stream source,
            string identifier,
            string mediaType,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new IngestionDocument(_name));
        }
    }
}
