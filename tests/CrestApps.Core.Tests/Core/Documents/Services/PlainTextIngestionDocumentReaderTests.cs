using System.Text;
using CrestApps.Core.AI.Documents.Services;

namespace CrestApps.Core.Tests.Core.Documents.Services;

public sealed class PlainTextIngestionDocumentReaderTests
{
    private const string PlainTextMediaType = "text/plain";

    [Fact]
    public async Task ReadAsync_EmptyStream_ReturnsDocumentWithNoSections()
    {
        var reader = new PlainTextIngestionDocumentReader();
        await using var source = new MemoryStream();

        var document = await reader.ReadAsync(source, "empty.txt", PlainTextMediaType, TestContext.Current.CancellationToken);

        Assert.NotNull(document);
        Assert.Empty(document.Sections);
    }

    [Fact]
    public async Task ReadAsync_WhitespaceOnly_ReturnsDocumentWithNoSections()
    {
        var reader = new PlainTextIngestionDocumentReader();
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("   \r\n\t  "));

        var document = await reader.ReadAsync(source, "blank.txt", PlainTextMediaType, TestContext.Current.CancellationToken);

        Assert.Empty(document.Sections);
    }

    [Fact]
    public async Task ReadAsync_PreCanceledToken_ThrowsOperationCanceled()
    {
        var reader = new PlainTextIngestionDocumentReader();
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ReadAsync(source, "cancel.txt", PlainTextMediaType, cts.Token));
    }

    [Fact]
    public async Task ReadAsync_HappyPath_ReturnsSingleParagraphWithText()
    {
        var reader = new PlainTextIngestionDocumentReader();
        var content = "Hello, plain text world!";
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var document = await reader.ReadAsync(source, "hello.txt", PlainTextMediaType, TestContext.Current.CancellationToken);

        var section = Assert.Single(document.Sections);
        var paragraph = Assert.Single(section.Elements);
        var typed = Assert.IsType<Microsoft.Extensions.DataIngestion.IngestionDocumentParagraph>(paragraph);
        Assert.Equal(content, typed.Text);
    }
}
