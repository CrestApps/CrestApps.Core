using CrestApps.Core.AI.Documents.Pdf.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Reading;

public sealed class ComparePdfsToolTests
{
    private const string Opening = "Revenue rose by twelve percent against the plan for the quarter.";
    private const string Middle = "The second paragraph stays the same in both versions of the file.";

    [Fact]
    public async Task Compare_WordGranularity_ShowsTheWordsThatChanged()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("v1.pdf", await PdfReadingFixtures.ParagraphsAsync(Opening, Middle, "This line is removed later."));
        await host.UploadAsync("v2.pdf", await PdfReadingFixtures.ParagraphsAsync(Opening.Replace("twelve", "fifteen", StringComparison.Ordinal), Middle, "A new closing line was added."));

        var result = await host.InvokeAsync(new ComparePdfsTool(), new { pdf = "v1.pdf", other = "v2.pdf", granularity = "word" });

        Assert.Contains("Compared \"v2.pdf\" with the baseline \"v1.pdf\"", result, StringComparison.Ordinal);
        Assert.Contains("2 change(s): 1 line(s) changed, 1 added, 1 removed.", result, StringComparison.Ordinal);
        Assert.Contains("Revenue rose by [-twelve-] {+fifteen+} percent", result, StringComparison.Ordinal);
        Assert.Contains("p.1 ↔ p.1", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compare_LineGranularity_ListsRemovedAndAddedLines()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("v1.pdf", await PdfReadingFixtures.ParagraphsAsync(Opening, Middle));
        await host.UploadAsync("v2.pdf", await PdfReadingFixtures.ParagraphsAsync(Opening, Middle, "---", "An appendix page was added to the second version."));

        var result = await host.InvokeAsync(new ComparePdfsTool(), new { pdfs = new[] { "v1.pdf", "v2.pdf" } });

        Assert.Contains("- Pages: 1 vs 2.", result, StringComparison.Ordinal);
        Assert.Contains("1. added — p.1 ↔ p.2", result, StringComparison.Ordinal);
        Assert.Contains("   + An appendix page was added to the second version.", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compare_SameText_SaysSoAndReportsPropertyDifferences()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("a.pdf", await PdfReadingFixtures.ReportAsync());
        await host.UploadAsync("b.pdf", await PdfReadingFixtures.ReportAsync(language: "fr-FR"));

        var result = await host.InvokeAsync(new ComparePdfsTool(), new { pdfs = "a.pdf, b.pdf" });

        Assert.Contains("- The text is the same.", result, StringComparison.Ordinal);
        Assert.DoesNotContain("differs", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compare_DifferentPaper_IsReported()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("a4.pdf", await PdfReadingFixtures.ReportAsync());
        await host.UploadAsync("letter.pdf", PdfReadingFixtures.LinksAndBookmarks());

        var result = await host.InvokeAsync(new ComparePdfsTool(), new { pdfs = new[] { "a4.pdf", "letter.pdf" } });

        Assert.Contains("Page size differs on 2 page(s): page 1: A4 portrait vs Letter portrait", result, StringComparison.Ordinal);
        Assert.Contains("The title differs: \"Quarterly Report\" → \"Links sample\".", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compare_OneDocument_AsksForAnother()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("a.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new ComparePdfsTool(), new { pdf = "a.pdf" });

        Assert.Contains("Name at least two PDFs to compare", result, StringComparison.Ordinal);
    }
}
