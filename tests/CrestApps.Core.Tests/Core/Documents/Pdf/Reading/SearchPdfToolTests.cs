using CrestApps.Core.AI.Documents.Pdf.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Reading;

public sealed class SearchPdfToolTests
{
    [Fact]
    public async Task Search_PlainText_CountsMatchesPerPageWithBoxes()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new SearchPdfTool(), new { query = "revenue" });

        Assert.Contains("Found 4 match(es) for \"revenue\"", result, StringComparison.Ordinal);
        Assert.Contains("Matches per page: p1: 2, p2: 1, p3: 1", result, StringComparison.Ordinal);
        Assert.Contains("1. p1 \"Revenue\" — ", result, StringComparison.Ordinal);
        Assert.Contains("[x=", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_RegexAndMatchCase_FindOnlyWhatTheyDescribe()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var numbers = await host.InvokeAsync(new SearchPdfTool(), new { query = @"\b\d{4,5}\b", regex = true });
        var upper = await host.InvokeAsync(new SearchPdfTool(), new { query = "Revenue", match_case = true });

        Assert.Contains("\"74612\"", numbers, StringComparison.Ordinal);
        Assert.Contains("\"5300\"", numbers, StringComparison.Ordinal);
        Assert.Contains("Found 2 match(es)", upper, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_WholeWord_IgnoresPartsOfWords()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var partial = await host.InvokeAsync(new SearchPdfTool(), new { query = "revenu" });
        var whole = await host.InvokeAsync(new SearchPdfTool(), new { query = "revenu", whole_word = true });

        Assert.Contains("Found 4 match(es)", partial, StringComparison.Ordinal);
        Assert.Contains("No matches for \"revenu\"", whole, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_MaxResults_ListsFewerButCountsAll()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new SearchPdfTool(), new { query = "the", max_results = 1 });

        Assert.Contains("1. p1", result, StringComparison.Ordinal);
        Assert.DoesNotContain("\n2. p", result, StringComparison.Ordinal);
        Assert.Contains("Listed the first 1 of", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_WithoutQuery_AsksForOne()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new SearchPdfTool(), new { pages = "1" });

        Assert.Contains("Pass 'query'", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_InvalidRegex_IsExplained()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new SearchPdfTool(), new { query = "(unclosed", regex = true });

        Assert.Contains("is not a valid regular expression", result, StringComparison.Ordinal);
    }
}
