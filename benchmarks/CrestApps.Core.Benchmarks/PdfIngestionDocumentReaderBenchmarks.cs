using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Documents.Pdf.Services;
using Microsoft.Extensions.DataIngestion;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures PDF ingestion from seekable and non-seekable sources.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class PdfIngestionDocumentReaderBenchmarks
{
    private const string PdfMediaType = "application/pdf";
    private const string JpegBase64 = "/9j/4AAQSkZJRgABAQAASABIAAD/4QBMRXhpZgAATU0AKgAAAAgAAYdpAAQAAAABAAAAGgAAAAAAA6ABAAMAAAABAAEAAKACAAQAAAABAAAAAaADAAQAAAABAAAAAQAAAAD/7QA4UGhvdG9zaG9wIDMuMAA4QklNBAQAAAAAAAA4QklNBCUAAAAAABDUHYzZjwCyBOmACZjs+EJ+/8AAEQgAAQABAwEiAAIRAQMRAf/EAB8AAAEFAQEBAQEBAAAAAAAAAAABAgMEBQYHCAkKC//EALUQAAIBAwMCBAMFBQQEAAABfQECAwAEEQUSITFBBhNRYQcicRQygZGhCCNCscEVUtHwJDNicoIJChYXGBkaJSYnKCkqNDU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6g4SFhoeIiYqSk5SVlpeYmZqio6Slpqeoqaqys7S1tre4ubrCw8TFxsfIycrS09TV1tfY2drh4uPk5ebn6Onq8fLz9PX29/j5+v/EAB8BAAMBAQEBAQEBAQEAAAAAAAABAgMEBQYHCAkKC//EALURAAIBAgQEAwQHBQQEAAECdwABAgMRBAUhMQYSQVEHYXETIjKBCBRCkaGxwQkjM1LwFWJy0QoWJDThJfEXGBkaJicoKSo1Njc4OTpDREVGR0hJSlNUVVZXWFlaY2RlZmdoaWpzdHV2d3h5eoKDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uLj5OXm5+jp6vLz9PX29/j5+v/bAEMAAgICAgICAwICAwUDAwMFBgUFBQUGCAYGBgYGCAoICAgICAgKCgoKCgoKCgwMDAwMDA4ODg4ODw8PDw8PDw8PD//bAEMBAgICBAQEBwQEBxALCQsQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEP/dAAQAAf/aAAwDAQACEQMRAD8A/fyiiigD/9k=";

    private readonly PdfIngestionDocumentReader _reader = new();
    private MemoryStream _innerStream;
    private Stream _source;

    /// <summary>
    /// Gets or sets the number of PDF pages.
    /// </summary>
    [Params(20)]
    public int PageCount { get; set; }

    /// <summary>
    /// Gets or sets the JPEG payload size used to represent image-heavy PDFs.
    /// </summary>
    [Params(0, 10)]
    public int PayloadMegabytes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the input stream supports seeking.
    /// </summary>
    [Params(true, false)]
    public bool Seekable { get; set; }

    /// <summary>
    /// Creates the representative PDF input.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _innerStream = new MemoryStream(CreatePdf(PageCount, PayloadMegabytes), writable: false);
        _source = Seekable
            ? _innerStream
            : new NonSeekableReadStream(_innerStream);
    }

    /// <summary>
    /// Rewinds the reusable input before each measured read.
    /// </summary>
    [IterationSetup]
    public void ResetSource()
    {
        _innerStream.Position = 0;
    }

    /// <summary>
    /// Disposes the reusable benchmark input.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _innerStream.Dispose();
    }

    /// <summary>
    /// Reads the representative PDF with the original always-buffered implementation.
    /// </summary>
    /// <returns>The ingestion document.</returns>
    [Benchmark(Baseline = true)]
    public async Task<IngestionDocument> ReadBufferedAsync()
    {
        if (_source.CanSeek)
        {
            _source.Position = 0;
        }

        await using var buffer = new MemoryStream();
        await _source.CopyToAsync(buffer);
        buffer.Position = 0;

        return ReadPdf(buffer);
    }

    /// <summary>
    /// Reads the representative PDF with the optimized implementation.
    /// </summary>
    /// <returns>The ingestion document.</returns>
    [Benchmark]
    public Task<IngestionDocument> ReadOptimizedAsync()
    {
        return _reader.ReadAsync(_source, "benchmark.pdf", PdfMediaType);
    }

    /// <summary>
    /// Creates a valid PDF containing text and an optional large JPEG payload.
    /// </summary>
    /// <param name="pageCount">The number of pages.</param>
    /// <param name="payloadMegabytes">The JPEG payload size.</param>
    /// <returns>The generated PDF bytes.</returns>
    private static byte[] CreatePdf(int pageCount, int payloadMegabytes)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var text = new string('A', 1000);

        for (var i = 0; i < pageCount; i++)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText($"Page {i}: {text}", 10, new PdfPoint(25, 700), font);

            if (i == 0 && payloadMegabytes > 0)
            {
                page.AddJpeg(
                    CreateJpegPayload(payloadMegabytes),
                    new PdfRectangle(25, 25, 125, 125));
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates a valid JPEG stream with an opaque trailing payload.
    /// </summary>
    /// <param name="payloadMegabytes">The payload size.</param>
    /// <returns>The JPEG bytes.</returns>
    private static byte[] CreateJpegPayload(int payloadMegabytes)
    {
        var jpeg = Convert.FromBase64String(JpegBase64);
        var payload = new byte[payloadMegabytes * 1024 * 1024];
        jpeg.CopyTo(payload, 0);

        return payload;
    }

    /// <summary>
    /// Reproduces the original PDF extraction behavior for the benchmark baseline.
    /// </summary>
    /// <param name="source">The buffered PDF source.</param>
    /// <returns>The extracted ingestion document.</returns>
    private static IngestionDocument ReadPdf(Stream source)
    {
        using var pdf = PdfDocument.Open(source);
        var document = new IngestionDocument("benchmark.pdf");

        for (var pageNumber = 1; pageNumber <= pdf.NumberOfPages; pageNumber++)
        {
            var page = pdf.GetPage(pageNumber);
            var pageText = page.Text?.Trim();

            if (string.IsNullOrWhiteSpace(pageText))
            {
                continue;
            }

            var section = new IngestionDocumentSection
            {
                PageNumber = page.Number,
            };

            section.Elements.Add(new IngestionDocumentParagraph(pageText)
            {
                Text = pageText,
            });
            document.Sections.Add(section);
        }

        return document;
    }

    /// <summary>
    /// Exposes a readable stream without seek support.
    /// </summary>
    private sealed class NonSeekableReadStream : Stream
    {
        private readonly Stream _inner;

        /// <summary>
        /// Initializes a new instance of the <see cref="NonSeekableReadStream"/> class.
        /// </summary>
        /// <param name="inner">The underlying stream.</param>
        public NonSeekableReadStream(Stream inner)
        {
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            return _inner.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return _inner.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
