using CrestApps.Core.AI.Documents.Pdf;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Reading;

public sealed class ExtractPdfTextToolTests
{
    [Fact]
    public async Task ExtractText_ReadsEveryPageInReadingOrder()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ExtractPdfTextTool());

        Assert.Contains("--- Page 1 ---", result, StringComparison.Ordinal);
        Assert.Contains("--- Page 3 ---", result, StringComparison.Ordinal);

        // MigraDoc writes no space glyphs; the words still come out separated.
        Assert.Contains("Revenue rose by twelve percent", result, StringComparison.Ordinal);
        Assert.True(
            result.IndexOf("Quarterly Report", StringComparison.Ordinal) < result.IndexOf("Regional results", StringComparison.Ordinal),
            "The heading is read before the section under it.");
        Assert.Contains("This appendix lists the methods", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractText_LinesWithFonts_ReportsBoxesAndStyles()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ExtractPdfTextTool(), new { pages = "1", format = "lines", include_fonts = true });

        Assert.Contains("Quarterly Report  [x=", result, StringComparison.Ordinal);
        Assert.Contains("20pt bold", result, StringComparison.Ordinal);
        Assert.Contains("10.5pt", result, StringComparison.Ordinal);
        Assert.DoesNotContain("--- Page 2 ---", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractText_Words_ListsEachWordWithItsBox()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ExtractPdfTextTool(), new { pages = "3", format = "words" });

        Assert.Contains("Appendix  [x=", result, StringComparison.Ordinal);
        Assert.Contains("\nmethods  [x=", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractText_PageWithoutText_PointsToOcr()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("scan.pdf", PdfReadingFixtures.TextAndBlankPage("A page with a few words on it."));

        var result = await host.InvokeAsync(new ExtractPdfTextTool());

        Assert.Contains("A page with a few words on it.", result, StringComparison.Ordinal);
        Assert.Contains("(no extractable text — the page may be scanned; ocr_pdf can read it)", result, StringComparison.Ordinal);
        Assert.Contains("Pages without extractable text: 2", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractText_LongerThanAnAnswer_SaysWhichPagesToAskForNext()
    {
        using var host = new PdfToolTestHost(configure: services => services.Configure<PdfAgentOptions>(options => options.MaxToolResponseCharacters = 1_600));

        var paragraph = string.Join(' ', Enumerable.Repeat("The quarterly figures were reviewed by the board and approved without changes.", 6));
        await host.UploadAsync("long.pdf", await PdfReadingFixtures.ParagraphsAsync(paragraph, "---", paragraph, "---", paragraph, "---", paragraph));

        var result = await host.InvokeAsync(new ExtractPdfTextTool());

        Assert.Contains("to stay within the answer size", result, StringComparison.Ordinal);
        Assert.Matches("Ask for pages \"[2-4]-4\" to continue", result);
        Assert.True(result.Length <= 1_600, "The answer stays within the configured size.");
    }

    [Fact]
    public async Task ExtractText_UnknownFormat_IsDeclined()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ExtractPdfTextTool(), new { format = "paragraphs" });

        Assert.Contains("is not a text format", result, StringComparison.Ordinal);
    }
}
