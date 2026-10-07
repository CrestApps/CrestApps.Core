using CrestApps.Core.AI.Documents.Pdf.Tools;
using UglyToad.PdfPig;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

/// <summary>
/// The follow-ups a smaller model makes after building a report — recolouring it, adding links and bookmarks,
/// naming it loosely — which used to leave the changes out of the export and pile up file copies.
/// </summary>
public sealed class PdfFollowUpTests
{
    [Fact]
    public async Task FileChangesToAComposedDocument_GoToOneFinishedFile_AndFormattingStillReachesTheExport()
    {
        using var host = new PdfToolTestHost();
        await CreateReportAsync(host);

        // File-only changes to the document being composed: both land in one finished file.
        var bookmarks = await host.InvokeAsync(new AddPdfBookmarksTool(), new
        {
            pdf = "Contoso Quarterly Report",
            bookmarks = new[] { new { title = "Revenue table", page = 2 } },
        });

        Assert.Contains("finished file \"Contoso Quarterly Report-edited\"", bookmarks, StringComparison.Ordinal);

        var links = await host.InvokeAsync(new AddPdfLinksTool(), new
        {
            pdf = "Contoso Quarterly Report",
            links = new[] { new { text = "Revenue Analysis", url = "#Revenue Analysis", occurrence = 1 } },
        });

        Assert.DoesNotContain("only http, https and mailto", links, StringComparison.Ordinal);
        Assert.Contains("→ page 3", links, StringComparison.Ordinal);

        var workspace = await host.LoadWorkspaceAsync();

        Assert.Equal(["Contoso Quarterly Report", "Contoso Quarterly Report-edited"], workspace.Documents.Select(document => document.Name));
        Assert.Equal("Contoso Quarterly Report", workspace.ActiveDocument);

        // The export of the document carries the file changes.
        await host.InvokeAsync(new ExportPdfTool(), new { });

        var (_, exported) = await host.ReadMarkerAsync("[doc:1]");

        using (var pdf = PdfDocument.Open(exported))
        {
            Assert.True(pdf.TryGetBookmarks(out var outline));
            Assert.Contains(outline.GetNodes(), node => node.Title == "Revenue table");
        }

        // Formatting, even when it names the finished file, changes the document itself.
        var formatted = await host.InvokeAsync(new FormatPdfTool(), new
        {
            pdf = "Contoso Quarterly Report-edited",
            cover_page = new { text_color = "#2E7D32" },
        });

        Assert.Contains("so the change was made to \"Contoso Quarterly Report\"", formatted, StringComparison.Ordinal);
        Assert.Contains("are not in this version", formatted, StringComparison.Ordinal);

        await host.InvokeAsync(new ExportPdfTool(), new { file_name = "green" });

        var (_, green) = await host.ReadMarkerAsync("[doc:2]");

        using var greenPdf = PdfDocument.Open(green);
        var title = greenPdf.GetPage(1).Letters.First(letter => letter.Value == "C");

        Assert.Equal((46 / 255d, 125 / 255d, 50 / 255d), title.Color.ToRGBValues(), new ColorComparer());
    }

    [Fact]
    public async Task LooseNames_UnknownSections_AndMetadata_AreHandledInsteadOfFailing()
    {
        using var host = new PdfToolTestHost();
        await CreateReportAsync(host);

        var info = await host.InvokeAsync(new GetPdfPageContentTool(), new { pdf = "Contoso Quarterly Report PDF", page = 1 });

        Assert.DoesNotContain("There is no PDF named", info, StringComparison.Ordinal);

        var added = await host.InvokeAsync(new AddPdfContentTool(), new
        {
            pdf = "contoso_quarterly_report",
            section = "s2",
            blocks = new object[] { new { type = "paragraph", text = "Outlook: steady growth." } },
        });

        Assert.Contains("to section s1", added, StringComparison.Ordinal);

        var metadata = await host.InvokeAsync(new EditPdfMetadataTool(), new { pdf = "Contoso Quarterly Report", author = "Finance team" });

        Assert.Contains("the document being composed", metadata, StringComparison.Ordinal);

        var workspace = await host.LoadWorkspaceAsync();

        Assert.Single(workspace.Documents);
        Assert.Equal("Finance team", workspace.Documents[0].Definition.Author);
    }

    [Fact]
    public async Task StartAtOne_BehindACover_NumbersTheBodyOnly_SoTheNumberNeverPassesTheTotal()
    {
        using var host = new PdfToolTestHost();

        await host.InvokeAsync(new CreatePdfTool(), new
        {
            name = "numbered",
            title = "Numbered",
            cover_page = new { },
            page_numbers = new { enabled = true, start_at = 1 },
            blocks = new object[]
            {
                new { type = "paragraph", text = "First body page." },
                new { type = "page_break" },
                new { type = "paragraph", text = "Second body page." },
            },
        });

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("numbered"));

        string Words(int page)
        {
            return string.Join(' ', pdf.GetPage(page).GetWords().Select(word => word.Text));
        }

        Assert.Equal(3, pdf.NumberOfPages);
        Assert.Contains("Page 1 of 2", Words(2), StringComparison.Ordinal);
        Assert.Contains("Page 2 of 2", Words(3), StringComparison.Ordinal);
    }

    private static Task<string> CreateReportAsync(PdfToolTestHost host)
    {
        return host.InvokeAsync(new CreatePdfTool(), new
        {
            name = "Contoso Quarterly Report",
            title = "Contoso Quarterly Report",
            cover_page = new { subtitle = "Third quarter" },
            page_numbers = new { enabled = true },
            blocks = new object[]
            {
                new { type = "heading", text = "Summary", level = 1 },
                new { type = "paragraph", text = "See Revenue Analysis below." },
                new { type = "page_break" },
                new { type = "heading", text = "Revenue Analysis", level = 1 },
                new { type = "paragraph", text = "Revenue rose in every quarter." },
            },
        });
    }

    private sealed class ColorComparer : IEqualityComparer<(double, double, double)>
    {
        public bool Equals((double, double, double) x, (double, double, double) y)
        {
            return Math.Abs(x.Item1 - y.Item1) < 0.01 && Math.Abs(x.Item2 - y.Item2) < 0.01 && Math.Abs(x.Item3 - y.Item3) < 0.01;
        }

        public int GetHashCode((double, double, double) obj)
        {
            return 0;
        }
    }
}
