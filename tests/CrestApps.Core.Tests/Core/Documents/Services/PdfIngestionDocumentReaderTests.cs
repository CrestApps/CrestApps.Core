using CrestApps.Core.AI.Documents.Pdf.Services;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace CrestApps.Core.Tests.Core.Documents.Services;

public sealed class PdfIngestionDocumentReaderTests
{
    private const string PdfMediaType = "application/pdf";

    [Fact]
    public async Task ReadAsync_UnsupportedMediaType_ThrowsNotSupportedException()
    {
        var reader = new PdfIngestionDocumentReader();
        await using var source = new MemoryStream();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => reader.ReadAsync(source, "file.txt", "text/plain", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_EmptyStream_ThrowsForPdfMediaType()
    {
        var reader = new PdfIngestionDocumentReader();
        await using var source = new MemoryStream();

        await Assert.ThrowsAnyAsync<Exception>(
            () => reader.ReadAsync(source, "empty.pdf", PdfMediaType, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_CorruptBytes_ThrowsForPdfMediaType()
    {
        var reader = new PdfIngestionDocumentReader();
        var garbage = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x00, 0x01, 0x02, 0xFF, 0xFE };
        await using var source = new MemoryStream(garbage);

        await Assert.ThrowsAnyAsync<Exception>(
            () => reader.ReadAsync(source, "corrupt.pdf", PdfMediaType, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_PreCanceledToken_ThrowsOperationCanceled()
    {
        var reader = new PdfIngestionDocumentReader();
        await using var source = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ReadAsync(source, "cancel.pdf", PdfMediaType, cts.Token));
    }

    [Fact]
    public async Task ReadAsync_HappyPath_ReadsTextFromGeneratedPdf()
    {
        var reader = new PdfIngestionDocumentReader();
        await using var source = CreatePdfWithText("Hello PDF world");

        var document = await reader.ReadAsync(source, "hello.pdf", PdfMediaType, TestContext.Current.CancellationToken);

        var section = Assert.Single(document.Sections);
        Assert.Equal(1, section.PageNumber);
        var paragraph = Assert.IsType<Microsoft.Extensions.DataIngestion.IngestionDocumentParagraph>(
            Assert.Single(section.Elements));
        Assert.Contains("Hello PDF world", paragraph.Text);
    }

    private static MemoryStream CreatePdfWithText(string text)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText(text, 12, new PdfPoint(25, 700), font);

        var bytes = builder.Build();

        return new MemoryStream(bytes);
    }
}
