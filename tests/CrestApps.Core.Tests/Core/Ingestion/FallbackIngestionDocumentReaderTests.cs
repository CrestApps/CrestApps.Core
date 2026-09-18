using System.Text;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Processors;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Ingestion;

public sealed class FallbackIngestionDocumentReaderTests
{
    /// <summary>
    /// Verifies that the preferred reader is used when it works, and the fallback is never touched.
    /// </summary>
    [Fact]
    public async Task ReadAsync_PreferredSucceeds_FallbackIsNotUsed()
    {
        var fallback = new RecordingReader("fallback");
        var reader = Create(new RecordingReader("preferred"), fallback);

        var document = await ReadAsync(reader);

        Assert.Equal("preferred", document.Sections[0].Elements[0].Text);
        Assert.Equal(0, fallback.Calls);
    }

    /// <summary>
    /// Verifies that a failing service costs the document nothing. A paid reader being unavailable is never a
    /// reason to reject a file.
    /// </summary>
    [Fact]
    public async Task ReadAsync_PreferredThrows_FallsBackAndKeepsReading()
    {
        var reader = Create(new ThrowingReader(new InvalidOperationException("service is down")), new RecordingReader("fallback"));

        var document = await ReadAsync(reader);

        Assert.Equal("fallback", document.Sections[0].Elements[0].Text);
    }

    /// <summary>
    /// Verifies that a media type the preferred reader does not handle simply routes to the fallback.
    /// </summary>
    [Fact]
    public async Task ReadAsync_PreferredNotSupported_FallsBack()
    {
        var reader = Create(new ThrowingReader(new NotSupportedException("not configured")), new RecordingReader("fallback"));

        var document = await ReadAsync(reader);

        Assert.Equal("fallback", document.Sections[0].Elements[0].Text);
    }

    /// <summary>
    /// Verifies that the fallback receives the whole content. The preferred reader consumes the stream, so
    /// the bytes have to survive the first attempt.
    /// </summary>
    [Fact]
    public async Task ReadAsync_PreferredConsumedTheStream_FallbackStillSeesAllBytes()
    {
        var fallback = new RecordingReader("fallback");
        var reader = Create(new ConsumingThrowingReader(), fallback);

        await ReadAsync(reader);

        Assert.Equal("the original content", fallback.ObservedContent);
    }

    /// <summary>
    /// Verifies that cancellation is not treated as a service failure to be worked around.
    /// </summary>
    [Fact]
    public async Task ReadAsync_Cancelled_DoesNotFallBack()
    {
        var fallback = new RecordingReader("fallback");
        var reader = Create(new ThrowingReader(new OperationCanceledException()), fallback);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("the original content"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ReadAsync(source, "doc.pdf", "application/pdf", cts.Token));

        Assert.Equal(0, fallback.Calls);
    }

    /// <summary>
    /// Verifies that a caption a provider reported is left alone. The caption processor exists to infer what
    /// a provider already knows, so it must not overwrite it with a guess.
    /// </summary>
    [Fact]
    public async Task CaptionProcessor_ProviderCaption_IsNotOverwritten()
    {
        var image = new IngestionDocumentImage("![](doc-p1-1)")
        {
            PageNumber = 1,
        };

        image.Metadata[FigureMetadataKeys.Id] = "doc-p1-1";
        image.Metadata[FigureMetadataKeys.Caption] = "Figure 3. The caption the service found.";
        image.Metadata[FigureMetadataKeys.CaptionSource] = CaptionSources.Provider;
        image.Metadata[ElementMetadataKeys.BoundingBox] = new[] { 40d, 500d, 300d, 650d };

        var candidate = new IngestionDocumentParagraph("Figure 9. A nearby label.")
        {
            Text = "Figure 9. A nearby label.",
            PageNumber = 1,
        };

        candidate.Metadata[ElementMetadataKeys.BoundingBox] = new[] { 40d, 480d, 300d, 492d };
        candidate.Metadata[ElementMetadataKeys.ModalPointSize] = 9d;
        candidate.Metadata[ElementMetadataKeys.ModalFontName] = "BodyFont";

        var section = new IngestionDocumentSection
        {
            PageNumber = 1,
        };

        section.Elements.Add(image);
        section.Elements.Add(candidate);

        var document = new IngestionDocument("doc");
        document.Sections.Add(section);

        var options = Options.Create(new CaptionPatternOptions());
        var processor = new FigureCaptionProcessor(
            new DefaultFigureCaptionCandidateDetector(options),
            new DefaultFigureCaptionResolver(options),
            options);

        await processor.ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal("Figure 3. The caption the service found.", image.GetMetadataString(FigureMetadataKeys.Caption));
        Assert.Equal(CaptionSources.Provider, image.GetMetadataString(FigureMetadataKeys.CaptionSource));
    }

    /// <summary>
    /// Verifies that a provider caption counts as strongly as a printed numbered one, because that is exactly
    /// what it is.
    /// </summary>
    [Fact]
    public async Task Salience_ProviderCaption_ScoresAsPattern()
    {
        var image = new IngestionDocumentImage("![](doc-p1-1)")
        {
            Content = new byte[] { 1, 2, 3, 4 },
            MediaType = "image/png",
            PageNumber = 1,
        };

        image.Metadata[FigureMetadataKeys.Id] = "doc-p1-1";
        image.Metadata[FigureMetadataKeys.ContentHash] = "hash";
        image.Metadata[FigureMetadataKeys.PixelWidth] = 400;
        image.Metadata[FigureMetadataKeys.PixelHeight] = 300;
        image.Metadata[ElementMetadataKeys.BoundingBox] = new[] { 40d, 400d, 300d, 550d };
        image.Metadata[FigureMetadataKeys.Caption] = "Figure 3. The caption the service found.";
        image.Metadata[FigureMetadataKeys.CaptionSource] = CaptionSources.Provider;

        var section = new IngestionDocumentSection
        {
            PageNumber = 1,
        };

        section.Metadata[ElementMetadataKeys.PageWidth] = 595d;
        section.Metadata[ElementMetadataKeys.PageHeight] = 842d;
        section.Elements.Add(image);

        var document = new IngestionDocument("doc");
        document.Sections.Add(section);

        var processor = new FigureSalienceProcessor(
            Options.Create(new FigureSalienceOptions()),
            Options.Create(new CaptionPatternOptions()));

        await processor.ProcessAsync(document, DocumentIngestionContext.Default, TestContext.Current.CancellationToken);

        Assert.Equal(FigureTiers.Describe, image.GetMetadataString(FigureMetadataKeys.Tier));
    }

    private static FallbackIngestionDocumentReader Create(IngestionDocumentReader preferred, IngestionDocumentReader fallback)
    {
        return new FallbackIngestionDocumentReader(
            preferred,
            fallback,
            NullLogger<FallbackIngestionDocumentReader>.Instance);
    }

    private static async Task<IngestionDocument> ReadAsync(FallbackIngestionDocumentReader reader)
    {
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("the original content"));

        return await reader.ReadAsync(source, "doc.pdf", "application/pdf", TestContext.Current.CancellationToken);
    }

    private sealed class RecordingReader : IngestionDocumentReader
    {
        private readonly string _marker;

        public RecordingReader(string marker)
        {
            _marker = marker;
        }

        public int Calls { get; private set; }

        public string ObservedContent { get; private set; }

        public override async Task<IngestionDocument> ReadAsync(
            Stream source,
            string identifier,
            string mediaType,
            CancellationToken cancellationToken = default)
        {
            Calls++;

            using var streamReader = new StreamReader(source, Encoding.UTF8, leaveOpen: true);
            ObservedContent = await streamReader.ReadToEndAsync(cancellationToken);

            var document = new IngestionDocument(identifier);
            var section = new IngestionDocumentSection
            {
                PageNumber = 1,
            };

            section.Elements.Add(new IngestionDocumentParagraph(_marker)
            {
                Text = _marker,
                PageNumber = 1,
            });

            document.Sections.Add(section);

            return document;
        }
    }

    private sealed class ThrowingReader : IngestionDocumentReader
    {
        private readonly Exception _exception;

        public ThrowingReader(Exception exception)
        {
            _exception = exception;
        }

        public override Task<IngestionDocument> ReadAsync(
            Stream source,
            string identifier,
            string mediaType,
            CancellationToken cancellationToken = default)
        {
            throw _exception;
        }
    }

    /// <summary>
    /// Reads the whole stream before failing, which is what a real client does when it uploads the bytes and
    /// then gets an error back.
    /// </summary>
    private sealed class ConsumingThrowingReader : IngestionDocumentReader
    {
        public override async Task<IngestionDocument> ReadAsync(
            Stream source,
            string identifier,
            string mediaType,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);

            throw new InvalidOperationException("the service rejected the upload");
        }
    }
}
