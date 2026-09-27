using CrestApps.Core.AI.Documents.Pdf.Tools;
using UglyToad.PdfPig;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

public sealed class EditPdfPagesToolTests
{
    [Fact]
    public async Task MergeDeleteReorderRotate_ChangesTheWorkingCopyOnly()
    {
        using var host = new PdfToolTestHost();
        var original = await PdfToolFixtures.SimplePdfAsync("Alpha page", "Beta page", "Gamma page");
        var upload = await host.UploadAsync("main.pdf", original);
        await host.UploadAsync("appendix.pdf", await PdfToolFixtures.SimplePdfAsync("Appendix page"));

        var result = await host.InvokeAsync(new EditPdfPagesTool(), new
        {
            pdf = "main.pdf",
            operations = new object[]
            {
                new { operation = "merge", sources = new[] { "appendix.pdf" } },
                new { operation = "delete", pages = "2" },
                new { operation = "reorder", order = "3,1" },
                new { operation = "rotate", pages = "1", degrees = 90 },
            },
        });

        Assert.Contains("Saved as working PDF \"main\" (3 page(s))", result, StringComparison.Ordinal);
        Assert.Contains("The uploaded file was not changed", result, StringComparison.Ordinal);

        using var edited = PdfDocument.Open(await host.ReadWorkingPdfAsync("main"));

        Assert.Equal(3, edited.NumberOfPages);
        // Letters rather than words: the rotated first page is read by the word extractor letter by letter.
        Assert.Equal(
            ["Appendixpage", "Alphapage", "Gammapage"],
            Enumerable.Range(1, 3).Select(number => string.Concat(edited.GetPage(number).Letters.Select(letter => letter.Value)).Replace(" ", string.Empty, StringComparison.Ordinal)));
        Assert.Equal(90, edited.GetPage(1).Rotation.Value);

        // The upload itself is untouched.
        Assert.Equal(original, await host.ReadFileAsync(upload.StoredFilePath));

        // A second edit of the upload's name continues on the working copy.
        var second = await host.InvokeAsync(new EditPdfPagesTool(), new
        {
            pdf = "main",
            operations = new object[] { new { operation = "delete", pages = "last" } },
        });

        Assert.Contains("(2 page(s))", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WatermarkStampPageNumbersHeaderFooter_DrawOnEveryPage()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("contract.pdf", await PdfToolFixtures.SimplePdfAsync("One", "Two"));

        var result = await host.InvokeAsync(new EditPdfPagesTool(), new
        {
            operations = new object[]
            {
                new { operation = "watermark", text = "DRAFT" },
                new { operation = "stamp", text = "APPROVED", position = "top-right" },
                new { operation = "page_numbers", template = "Page {page} of {pages}" },
                new { operation = "header_footer", header = new { left = "{title}" } },
            },
        });

        Assert.Contains("Saved as working PDF \"contract\"", result, StringComparison.Ordinal);

        using var edited = PdfDocument.Open(await host.ReadWorkingPdfAsync("contract"));

        for (var number = 1; number <= 2; number++)
        {
            var page = edited.GetPage(number);
            var letters = string.Concat(page.Letters.Select(letter => letter.Value));
            var words = string.Join(' ', page.GetWords().Select(word => word.Text));

            Assert.Contains("DRAFT", letters, StringComparison.Ordinal);
            Assert.Contains("APPROVED", words, StringComparison.Ordinal);
            Assert.Contains($"Page {number} of 2", words, StringComparison.Ordinal);
            Assert.Contains("contract", words, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Split_SavesOneWorkingPdfPerPart()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("book.pdf", await PdfToolFixtures.SimplePdfAsync("A", "B", "C", "D", "E"));

        var result = await host.InvokeAsync(new EditPdfPagesTool(), new
        {
            operations = new object[] { new { operation = "split", ranges = new[] { "1-2", "3-5" } } },
        });

        Assert.Contains("\"book-part-1\" (2 page(s))", result, StringComparison.Ordinal);
        Assert.Contains("\"book-part-2\" (3 page(s))", result, StringComparison.Ordinal);

        using var second = PdfDocument.Open(await host.ReadWorkingPdfAsync("book-part-2"));

        Assert.Equal("C", second.GetPage(1).GetWords().First().Text);
    }

    [Fact]
    public async Task DuplicateInsertBlankCropResize_ProduceTheExpectedPages()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("sheet.pdf", await PdfToolFixtures.SimplePdfAsync("First", "Second"));

        var result = await host.InvokeAsync(new EditPdfPagesTool(), new
        {
            operations = new object[]
            {
                new { operation = "duplicate", pages = "1" },
                new { operation = "insert_blank", after = 0 },
                new { operation = "crop", pages = "2", margins = new { margin_top_mm = 10, margin_left_mm = 10 } },
                new { operation = "resize", size = "Letter" },
            },
        });

        Assert.Contains("(4 page(s))", result, StringComparison.Ordinal);

        using var edited = PdfDocument.Open(await host.ReadWorkingPdfAsync("sheet"));

        Assert.Equal(4, edited.NumberOfPages);
        Assert.Empty(edited.GetPage(1).GetWords());
        Assert.Equal("First", edited.GetPage(2).GetWords().First().Text);
        Assert.Equal("First", edited.GetPage(3).GetWords().First().Text);
        Assert.Equal(612, edited.GetPage(4).Width, 0);
        Assert.Equal(792, edited.GetPage(4).Height, 0);
    }

    [Fact]
    public async Task BadPage_ExplainsTheRange()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("short.pdf", await PdfToolFixtures.SimplePdfAsync("Only"));

        var result = await host.InvokeAsync(new EditPdfPagesTool(), new
        {
            operations = new object[] { new { operation = "rotate", pages = "4" } },
        });

        Assert.Contains("Page 4", result, StringComparison.Ordinal);
        Assert.Contains("1 page(s)", result, StringComparison.Ordinal);
        Assert.Empty((await host.LoadWorkspaceAsync()).Documents);
    }

    [Fact]
    public void PageRange_ParsesTheCommonForms()
    {
        Assert.Equal([1, 2, 3, 5], PdfPageRange.Parse("1-3,5", 6));
        Assert.Equal([5, 6], PdfPageRange.Parse("5-", 6));
        Assert.Equal([6], PdfPageRange.Parse("last", 6));
        Assert.Equal([1, 3, 5], PdfPageRange.Parse("odd", 6));
        Assert.Equal([3, 2, 1], PdfPageRange.Parse("3-1", 6, keepOrderAndDuplicates: true));
        Assert.Equal("1-3, 5", PdfPageRange.Describe([5, 1, 2, 3]));
    }
}
