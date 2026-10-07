using System.Text.Json;
using CrestApps.Core.AI.Documents.Pdf.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Reading;

public sealed class GeneratePdfOutlineToolTests
{
    [Fact]
    public async Task GenerateOutline_FromBookmarks_NestsEntriesWithPages()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("links.pdf", PdfReadingFixtures.LinksAndBookmarks());

        var result = await host.InvokeAsync(new GeneratePdfOutlineTool());

        Assert.Contains("from its bookmarks, 3 entries", result, StringComparison.Ordinal);
        Assert.Contains("- Introduction (p. 1)\n  - Links (p. 1)\n- Appendix (p. 2)", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateOutline_MaxDepth_LeavesOutDeeperBookmarks()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("links.pdf", PdfReadingFixtures.LinksAndBookmarks());

        var result = await host.InvokeAsync(new GeneratePdfOutlineTool(), new { max_depth = 1 });

        Assert.DoesNotContain("Links (p. 1)", result, StringComparison.Ordinal);
        Assert.Contains("1 bookmark(s) deeper than level 1 were left out", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateOutline_WithoutBookmarks_UsesHeadingsAndIgnoresRunningHeads()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("columns.pdf", PdfReadingFixtures.TwoColumnsWithRunningText());

        var result = await host.InvokeAsync(new GeneratePdfOutlineTool());

        Assert.Contains("from headings detected by type size (it has no bookmarks)", result, StringComparison.Ordinal);
        Assert.Contains("- Market overview (p. 1)\n- Regional detail (p. 2)\n- Outlook and risks (p. 3)", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Northwind", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateOutline_Headings_NestByTypeSize()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new GeneratePdfOutlineTool(), new { source = "headings", format = "json" });
        var json = result.Split('\n').Single(line => line.StartsWith('['));

        using var parsed = JsonDocument.Parse(json);
        var roots = parsed.RootElement.EnumerateArray().ToList();

        Assert.Equal(["Quarterly Report", "Outlook", "Appendix"], roots.Select(root => root.GetProperty("title").GetString()));
        Assert.Equal("Regional results", roots[0].GetProperty("children")[0].GetProperty("title").GetString());
        Assert.Equal(3, roots[2].GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task GenerateOutline_BookmarksOnlyWhenThereAreNone_SaysSo()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("columns.pdf", PdfReadingFixtures.TwoColumnsWithRunningText());

        var result = await host.InvokeAsync(new GeneratePdfOutlineTool(), new { source = "bookmarks" });

        Assert.Contains("has no bookmarks", result, StringComparison.Ordinal);
    }
}
