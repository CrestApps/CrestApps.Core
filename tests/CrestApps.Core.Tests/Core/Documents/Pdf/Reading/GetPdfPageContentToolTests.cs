using CrestApps.Core.AI.Documents.Pdf.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Reading;

public sealed class GetPdfPageContentToolTests
{
    [Fact]
    public async Task GetPageContent_DescribesEverythingOnThePage()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("links.pdf", PdfReadingFixtures.LinksAndBookmarks());

        var result = await host.InvokeAsync(new GetPdfPageContentTool(), new { page = 1 });

        Assert.Contains("Page 1 of 2 of \"links.pdf\": Letter portrait (612 × 792 pt), rotated 0°.", result, StringComparison.Ordinal);
        Assert.Contains("Text blocks in reading order", result, StringComparison.Ordinal);
        Assert.Contains("(Arial,Bold 20pt bold): Introduction", result, StringComparison.Ordinal);
        Assert.Contains("- Arial: 12pt;", result, StringComparison.Ordinal);
        Assert.Contains("Links (4):", result, StringComparison.Ordinal);
        Assert.Contains("- internal: \"Go to the appendix.\" → (page 2)", result, StringComparison.Ordinal);
        Assert.Contains("- Text, at x=400", result, StringComparison.Ordinal);
        Assert.Contains("contents \"Check the figures\", by Reviewer", result, StringComparison.Ordinal);
        Assert.Contains("Form fields on this page (0):", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPageContent_Include_LimitsTheSections()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new GetPdfPageContentTool(), new { page = 2, include = new[] { "images", "fonts" } });

        Assert.Contains("Pictures (1):", result, StringComparison.Ordinal);
        Assert.Contains("64×48 px, png, DeviceRGB", result, StringComparison.Ordinal);
        Assert.Contains("- Arial,Italic: 9pt;", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Text blocks", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Links (", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPageContent_TextOnly_ReturnsThePlainText()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new GetPdfPageContentTool(), new { page = 3, include = "text" });

        Assert.Contains("Text:\nAppendix", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Fonts (", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPageContent_PagePastTheEnd_IsExplained()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var missing = await host.InvokeAsync(new GetPdfPageContentTool(), new { page = 9 });
        var unknown = await host.InvokeAsync(new GetPdfPageContentTool(), new { page = 1, include = new[] { "colours" } });

        Assert.Contains("Page 9 does not exist; \"report.pdf\" has 3 page(s).", missing, StringComparison.Ordinal);
        Assert.Contains("\"colours\" is not something a page description includes", unknown, StringComparison.Ordinal);
    }
}
