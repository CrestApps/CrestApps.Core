using CrestApps.Core.AI.Documents.Pdf.Tools;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Outline;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Properties;

public sealed class AddPdfBookmarksToolTests
{
    [Fact]
    public async Task AddBookmarks_NestsByLevelAndReadsBack()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("report.pdf", PdfPropertiesFixtures.TextPdf(pages: 3));

        var answer = await host.InvokeAsync(new AddPdfBookmarksTool(), new
        {
            bookmarks = new object[]
            {
                new { title = "Summary", page = 1, bold = true },
                new { title = "Details", page = 2, level = 2 },
                new { title = "Appendix", page = 3, color = "navy", y = 100 },
            },
        });

        Assert.Contains("Added 3 bookmark(s)", answer, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("report"));

        Assert.True(pdf.TryGetBookmarks(out var bookmarks));
        Assert.Equal(["Summary", "Appendix"], bookmarks.Roots.Select(root => root.Title));

        var summary = Assert.IsType<DocumentBookmarkNode>(bookmarks.Roots[0]);
        var details = Assert.IsType<DocumentBookmarkNode>(Assert.Single(summary.Children));
        var appendix = Assert.IsType<DocumentBookmarkNode>(bookmarks.Roots[1]);

        Assert.Equal(1, summary.PageNumber);
        Assert.Equal("Details", details.Title);
        Assert.Equal(2, details.PageNumber);
        Assert.Equal(3, appendix.PageNumber);
        Assert.Equal(pdf.GetPage(3).Height - 100, appendix.Destination.Coordinates.Top!.Value, 1);

        var listing = await host.InvokeAsync(new AddPdfBookmarksTool(), new { mode = "list" });

        Assert.Contains("has 3 bookmark(s)", listing, StringComparison.Ordinal);
        Assert.Contains("  - Details (page 2, level 2)", listing, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddBookmarks_AddAppendsReplaceSwapsAndClearRemoves()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("report.pdf", PdfPropertiesFixtures.TextPdf(pages: 2));

        await host.InvokeAsync(new AddPdfBookmarksTool(), new { bookmarks = new[] { new { title = "One", page = 1 } } });

        var appended = await host.InvokeAsync(new AddPdfBookmarksTool(), new { bookmarks = new[] { new { title = "Two", page = 2 } } });

        Assert.Contains("after the 1 it had", appended, StringComparison.Ordinal);
        Assert.Equal(["One", "Two"], await RootsAsync(host));

        await host.InvokeAsync(new AddPdfBookmarksTool(), new { mode = "replace", bookmarks = new[] { new { title = "Only", page = 2 } } });

        Assert.Equal(["Only"], await RootsAsync(host));

        var cleared = await host.InvokeAsync(new AddPdfBookmarksTool(), new { mode = "clear" });

        Assert.Contains("Removed all 1 bookmark(s)", cleared, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("report"));

        Assert.False(pdf.TryGetBookmarks(out var bookmarks) && bookmarks.Roots.Count > 0);
    }

    [Fact]
    public async Task AddBookmarks_FromHeadings_BuildsOutlineWithoutRunningHeads()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("review.pdf", PdfPropertiesFixtures.HeadingsPdf());

        var answer = await host.InvokeAsync(new AddPdfBookmarksTool(), new { mode = "from_headings" });

        Assert.Contains("\"Annual Review\" was taken as the document's title", answer, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("review"));

        Assert.True(pdf.TryGetBookmarks(out var bookmarks));
        Assert.Equal(["Introduction", "Results", "Outlook"], bookmarks.Roots.Select(root => root.Title));
        Assert.Equal(["Background", "Scope"], bookmarks.Roots[0].Children.Select(child => child.Title));
        Assert.Equal(["Revenue"], bookmarks.Roots[1].Children.Select(child => child.Title));
        Assert.Equal(2, ((DocumentBookmarkNode)bookmarks.Roots[1]).PageNumber);
        Assert.DoesNotContain(bookmarks.GetNodes(), node => node.Title.Contains("Contoso", StringComparison.Ordinal) || node.Title.StartsWith("Page", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddBookmarks_RejectsPagesPastTheEnd()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("report.pdf", PdfPropertiesFixtures.TextPdf(pages: 2));

        var answer = await host.InvokeAsync(new AddPdfBookmarksTool(), new { bookmarks = new[] { new { title = "Nowhere", page = 9 } } });

        Assert.Contains("points at page 9", answer, StringComparison.Ordinal);
        Assert.Empty((await host.LoadWorkspaceAsync()).Documents);
    }

    private static async Task<List<string>> RootsAsync(PdfToolTestHost host)
    {
        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("report"));

        Assert.True(pdf.TryGetBookmarks(out var bookmarks));

        return [.. bookmarks.Roots.Select(root => root.Title)];
    }
}
