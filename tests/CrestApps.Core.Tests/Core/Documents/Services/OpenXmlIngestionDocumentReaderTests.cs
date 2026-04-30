using CrestApps.Core.AI.Documents.OpenXml.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.Tests.Core.Documents.Services;

public sealed class OpenXmlIngestionDocumentReaderTests
{
    private const string WordMediaType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    [Fact]
    public async Task ReadAsync_UnsupportedMediaType_ThrowsNotSupportedException()
    {
        var reader = new OpenXmlIngestionDocumentReader();
        await using var source = new MemoryStream();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => reader.ReadAsync(source, "file.bin", "application/octet-stream", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_EmptyStream_ThrowsForSupportedMediaType()
    {
        var reader = new OpenXmlIngestionDocumentReader();
        await using var source = new MemoryStream();

        await Assert.ThrowsAnyAsync<Exception>(
            () => reader.ReadAsync(source, "empty.docx", WordMediaType, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_CorruptBytes_ThrowsForSupportedMediaType()
    {
        var reader = new OpenXmlIngestionDocumentReader();
        var garbage = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0xCA, 0xFE, 0xBA, 0xBE };
        await using var source = new MemoryStream(garbage);

        await Assert.ThrowsAnyAsync<Exception>(
            () => reader.ReadAsync(source, "corrupt.docx", WordMediaType, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_PreCanceledToken_ThrowsOperationCanceled()
    {
        var reader = new OpenXmlIngestionDocumentReader();
        await using var source = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ReadAsync(source, "cancel.docx", WordMediaType, cts.Token));
    }

    [Fact]
    public async Task ReadAsync_HappyPath_ReadsParagraphsFromWordDocument()
    {
        var reader = new OpenXmlIngestionDocumentReader();
        await using var source = CreateWordDocument("Hello DOCX world", "Second paragraph");

        var document = await reader.ReadAsync(source, "hello.docx", WordMediaType, TestContext.Current.CancellationToken);

        var section = Assert.Single(document.Sections);
        Assert.Equal(2, section.Elements.Count);
        var first = Assert.IsType<Microsoft.Extensions.DataIngestion.IngestionDocumentParagraph>(section.Elements[0]);
        var second = Assert.IsType<Microsoft.Extensions.DataIngestion.IngestionDocumentParagraph>(section.Elements[1]);
        Assert.Equal("Hello DOCX world", first.Text);
        Assert.Equal("Second paragraph", second.Text);
    }

    private static MemoryStream CreateWordDocument(params string[] paragraphs)
    {
        var stream = new MemoryStream();

        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();

            foreach (var text in paragraphs)
            {
                body.AppendChild(new Paragraph(new Run(new Text(text))));
            }

            mainPart.Document = new Document(body);
            mainPart.Document.Save();
        }

        stream.Position = 0;

        return stream;
    }
}
