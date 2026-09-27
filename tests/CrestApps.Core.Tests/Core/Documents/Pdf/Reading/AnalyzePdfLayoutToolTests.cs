using CrestApps.Core.AI.Documents.Pdf.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Reading;

public sealed class AnalyzePdfLayoutToolTests
{
    [Fact]
    public async Task AnalyzeLayout_TwoColumns_FindsColumnsRunningTextAndReadingOrder()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("columns.pdf", PdfReadingFixtures.TwoColumnsWithRunningText());

        var result = await host.InvokeAsync(new AnalyzePdfLayoutTool(), new { pages = "2" });

        Assert.Contains("Page 2 (A4 portrait) — 2 columns, 5 blocks, 0 figures, 1 heading", result, StringComparison.Ordinal);
        Assert.Contains("Columns: 1: x 60–", result, StringComparison.Ordinal);
        Assert.Contains("1. header [", result, StringComparison.Ordinal);
        Assert.Contains("heading 1 (18pt bold)", result, StringComparison.Ordinal);
        Assert.Contains("paragraph (column 2)", result, StringComparison.Ordinal);

        // The running foot closes the page's reading order, after the second column.
        Assert.Contains("5. footer [", result, StringComparison.Ordinal);
        Assert.True(
            result.IndexOf("(column 1)", StringComparison.Ordinal) < result.IndexOf("(column 2)", StringComparison.Ordinal),
            "The first column is read before the second.");
    }

    [Fact]
    public async Task AnalyzeLayout_FiguresTablesAndCaptions_AreReported()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new AnalyzePdfLayoutTool());

        Assert.Contains("Body text is set at 10.5pt; headings at 20pt (level 1), 15.2pt (level 2).", result, StringComparison.Ordinal);
        Assert.Contains("Table: 4 rows × 3 columns [", result, StringComparison.Ordinal);
        Assert.Contains("table text [", result, StringComparison.Ordinal);
        Assert.Contains("caption [", result, StringComparison.Ordinal);
        Assert.Contains("Figure: image 64×48 px [", result, StringComparison.Ordinal);
        Assert.Contains("— caption: Figure 1: Company logo", result, StringComparison.Ordinal);
        Assert.Contains("list item [", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeLayout_LongDocumentWithoutPages_AnalysesTheFirstFive()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("long.pdf", await PdfReadingFixtures.ParagraphsAsync("One.", "---", "Two.", "---", "Three.", "---", "Four.", "---", "Five.", "---", "Six.", "---", "Seven."));

        var result = await host.InvokeAsync(new AnalyzePdfLayoutTool());

        Assert.Contains("pages 1-5 of 7", result, StringComparison.Ordinal);
        Assert.Contains("Only the first 5 pages were analysed", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Page 6 (", result, StringComparison.Ordinal);
    }
}
