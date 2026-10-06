using System.Text;
using System.Xml.Linq;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using UglyToad.PdfPig;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

public sealed class PdfAuthoringToolsTests
{
    [Fact]
    public async Task CreatePdf_ReportAsAModelWritesIt_HasOneNumberPerPage_AGeneratedContentsPage_AndAReadableChart()
    {
        using var host = new PdfToolTestHost();

        // What a model wrote for "a 3-page quarterly report with a cover page, a table of contents and page
        // numbers in the footer": its own contents list, and the page number both in the footer and as
        // page_numbers.
        var created = await host.InvokeAsync(new CreatePdfTool(), Report());

        Assert.Contains("It lays out as 3 pages", created, StringComparison.Ordinal);
        Assert.Contains("replaced with a generated one", created, StringComparison.Ordinal);

        var workspace = await host.LoadWorkspaceAsync();
        var definition = Assert.Single(workspace.Documents).Definition;

        Assert.True(definition.TableOfContents?.Enabled);
        Assert.DoesNotContain(definition.Sections[0].Blocks, block => block.Type == "list");

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("contoso"));

        string Words(int page)
        {
            return string.Join(' ', pdf.GetPage(page).GetWords().Select(word => word.Text));
        }

        // Each page carries its number once, counted as the file counts pages.
        Assert.DoesNotContain("Page", Words(1), StringComparison.Ordinal);
        Assert.Equal(1, CountOf(Words(2), "Page 2 of 3"));
        Assert.Equal(1, CountOf(Words(3), "Page 3 of 3"));

        // The contents page lists the headings with the page they are on.
        Assert.Matches(@"Executive Summary[ .]*3", Words(2));

        // The chart's values read with thousands separators.
        Assert.Contains("190,000", Words(3), StringComparison.Ordinal);
        Assert.DoesNotContain("190000", Words(3), StringComparison.Ordinal);
    }

    internal static object Report()
    {
        return new
        {
            name = "contoso",
            title = "Contoso Quarterly Report",
            theme = new { heading_color = "#1F3A5F" },
            cover_page = new { enabled = true },
            page_numbers = new { enabled = true, position = "footer-center" },
            footer = new { center = "Page {page} of {pages}", separator = true },
            blocks = new object[]
            {
                new { type = "heading", text = "Table of Contents", level = 1 },
                new { type = "list", items = new[] { "Executive Summary", "Revenue Analysis" } },
                new { type = "page_break" },
                new { type = "heading", text = "Executive Summary", level = 1 },
                new { type = "paragraph", text = "This quarterly report gives an overview of revenue by region." },
                new { type = "heading", text = "Revenue Analysis", level = 1 },
                new
                {
                    type = "table",
                    table = new
                    {
                        columns = new object[]
                        {
                            new { header = "Region" },
                            new { header = "Q1 Revenue", format = "currency" },
                            new { header = "Q2 Revenue", format = "currency" },
                        },
                        rows = new[]
                        {
                            new[] { "North America", "190000", "215000" },
                            new[] { "Europe", "102500", "120000" },
                            new[] { "Asia-Pacific", "75625", "85000" },
                        },
                        total_row = new { label = "Total", functions = new Dictionary<string, string> { ["Q1 Revenue"] = "sum", ["Q2 Revenue"] = "sum" } },
                    },
                },
                new
                {
                    type = "chart",
                    chart = new
                    {
                        chart_type = "bar",
                        title = "Quarterly Revenue by Region",
                        labels = new[] { "North America", "Europe", "Asia-Pacific" },
                        series = new object[]
                        {
                            new { name = "Q1 Revenue", values = new[] { 190000, 102500, 75625 } },
                            new { name = "Q2 Revenue", values = new[] { 215000, 120000, 85000 } },
                        },
                        x_axis_title = "Region",
                        y_axis_title = "Revenue ($)",
                        data_labels = true,
                    },
                },
            },
        };
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;

        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public async Task CreatePdf_CoverPageWithoutTitle_IsStillDrawn_AndPreviewShowsThePagesThatExist()
    {
        using var host = new PdfToolTestHost();

        // The cover is asked for with only a subtitle and the document has no title: it used to be dropped
        // silently, turning a three-page report into two.
        var created = await host.InvokeAsync(new CreatePdfTool(), new
        {
            name = "quarterly",
            theme = new { heading_color = "#1F3A5F" },
            cover_page = new { enabled = true, subtitle = "Contoso quarterly report" },
            table_of_contents = new { enabled = true },
            page_numbers = new { enabled = true },
            blocks = new object[]
            {
                new { type = "heading", text = "Revenue", level = 1 },
                new { type = "paragraph", text = "Revenue grew in every quarter." },
            },
        });

        Assert.Contains("It lays out as 3 pages", created, StringComparison.Ordinal);
        Assert.Contains("The cover page has no title", created, StringComparison.Ordinal);
        Assert.Contains("headings #1F3A5F", created, StringComparison.Ordinal);

        using (var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("quarterly")))
        {
            Assert.Contains("Contoso", string.Join(' ', pdf.GetPage(1).GetWords().Select(word => word.Text)), StringComparison.Ordinal);
        }

        // Asking for more pages than there are shows the ones that exist instead of failing.
        var preview = await host.InvokeAsync(new PreviewPdfTool(), new { pages = "1-5" });

        Assert.Contains("[fig:1] [fig:2] [fig:3]", preview, StringComparison.Ordinal);
        Assert.Contains("page 4-5 does not exist", preview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePdf_AsPdfA_WritesTheArchiveIdentificationAndOutputIntent()
    {
        using var host = new PdfToolTestHost();

        var created = await host.InvokeAsync(new CreatePdfTool(), new
        {
            name = "archive",
            title = "Contoso & Northwind agreement",
            pdf_a = true,
            page_numbers = new { enabled = true },
            blocks = new object[]
            {
                new { type = "heading", text = "Terms", level = 1 },
                new { type = "paragraph", text = "See [the portal](https://example.com/terms) for the full terms." },
            },
        });

        Assert.Contains("Created working PDF \"archive\"", created, StringComparison.Ordinal);

        var verdict = await host.InvokeAsync(new ValidatePdfComplianceTool(), new { pdf = "archive", standard = "pdfa-2b" });

        Assert.Contains("Verdict: appears to satisfy PDF/A-2b", verdict, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("archive"));

        Assert.True(pdf.TryGetXmpMetadata(out var xmp));

        var packet = XDocument.Parse(Encoding.UTF8.GetString(xmp.GetXmlBytes().ToArray()));

        Assert.Contains(packet.Descendants(), element => element.Name.LocalName == "part" && element.Value == "2");

        // The ampersand in the title is escaped, so the packet is well-formed and carries the title.
        Assert.Contains(packet.Descendants(), element => element.Value == "Contoso & Northwind agreement");
    }

    [Fact]
    public async Task CreateAddFormatPreviewExport_BuildsTheRequestedReport()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("logo.png", PdfTestImages.RedSquarePng(64), "image/png");

        var created = await host.InvokeAsync(new CreatePdfTool(), new
        {
            name = "report",
            title = "Quarterly Report",
            theme = new { primary_color = "#0B5394", logo = "logo.png" },
            page_numbers = new { enabled = true },
            cover_page = new { subtitle = "Q3 results" },
            blocks = new object[]
            {
                new { type = "heading", text = "Summary", level = 1 },
                new { type = "paragraph", text = "Revenue rose by **12%** against plan." },
            },
        });

        Assert.Contains("Created working PDF \"report\"", created, StringComparison.Ordinal);
        Assert.Contains("b1 heading 1 \"Summary\"", created, StringComparison.Ordinal);

        var added = await host.InvokeAsync(new AddPdfContentTool(), new
        {
            blocks = new object[]
            {
                new
                {
                    type = "table",
                    table = new
                    {
                        columns = new object[] { new { header = "Region" }, new { header = "Revenue", format = "currency" } },
                        rows = new object[] { new object[] { "North", 1200.5 }, new object[] { "South", 800 } },
                        total_row = new { functions = new Dictionary<string, string> { ["Revenue"] = "sum" } },
                    },
                },
                new
                {
                    type = "chart",
                    chart = new { chart_type = "column", title = "Revenue by region", labels = new[] { "North", "South" }, series = new object[] { new { name = "Revenue", values = new[] { 1200.5, 800 } } } },
                },
            },
        });

        Assert.Contains("Added 2 blocks (b3–b4)", added, StringComparison.Ordinal);

        var replaced = await host.InvokeAsync(new AddPdfContentTool(), new
        {
            operation = "replace",
            target = "b2",
            blocks = new object[] { new { type = "paragraph", text = "Revenue rose by 14% against plan." } },
        });

        Assert.Contains("Replaced b2", replaced, StringComparison.Ordinal);

        var formatted = await host.InvokeAsync(new FormatPdfTool(), new
        {
            table_of_contents = new { title = "Contents" },
            footer = new { left = "{title}" },
        });

        Assert.Contains("Table of contents", formatted, StringComparison.Ordinal);
        Assert.Contains("primary #0B5394", formatted, StringComparison.Ordinal);

        var preview = await host.InvokeAsync(new PreviewPdfTool(), new { pages = "1-3" });

        Assert.Contains("[fig:1] [fig:2] [fig:3]", preview, StringComparison.Ordinal);

        var (figure, svgBytes) = await host.ReadMarkerAsync("[fig:1]");
        Assert.EndsWith(".svg", figure.FileName, StringComparison.Ordinal);

        // The preview must be well-formed markup, or the browser shows a broken picture.
        XDocument.Parse(Encoding.UTF8.GetString(svgBytes));

        var export = await host.InvokeAsync(new ExportPdfTool(), new { });

        Assert.Contains("[doc:1]", export, StringComparison.Ordinal);

        var (document, pdfBytes) = await host.ReadMarkerAsync("[doc:1]");
        Assert.Equal("report.pdf", document.FileName);

        using var pdf = PdfDocument.Open(pdfBytes);

        Assert.Equal(3, pdf.NumberOfPages);

        var body = string.Join(' ', pdf.GetPage(3).GetWords().Select(word => word.Text));

        Assert.Contains("14%", body, StringComparison.Ordinal);
        Assert.DoesNotContain("12%", body, StringComparison.Ordinal);
        Assert.Contains("$2,000.50", body, StringComparison.Ordinal);
        Assert.Contains("Page 3 of 3", body, StringComparison.Ordinal);
        Assert.Contains("Quarterly Report", body, StringComparison.Ordinal);

        var again = await host.InvokeAsync(new ExportPdfTool(), new { });
        Assert.Contains("already exported in this turn", again, StringComparison.Ordinal);

        var info = await host.InvokeAsync(new GetPdfInfoTool(), new { });
        Assert.Contains("\"report\" [active] — composed document", info, StringComparison.Ordinal);
        Assert.Contains("logo.png", info, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePdf_ExistingName_RequiresReplace()
    {
        using var host = new PdfToolTestHost();

        await host.InvokeAsync(new CreatePdfTool(), new { name = "memo" });
        var second = await host.InvokeAsync(new CreatePdfTool(), new { name = "memo" });

        Assert.Contains("already exists", second, StringComparison.Ordinal);

        var replaced = await host.InvokeAsync(new CreatePdfTool(), new { name = "memo", replace = true, title = "New memo" });

        Assert.Contains("Created working PDF \"memo\"", replaced, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddPdfContent_OnUploadedFile_ExplainsHowToChangeIt()
    {
        using var host = new PdfToolTestHost();
        var bytes = await PdfToolFixtures.SimplePdfAsync("Hello");
        await host.UploadAsync("letter.pdf", bytes);

        var result = await host.InvokeAsync(new AddPdfContentTool(), new
        {
            pdf = "letter.pdf",
            blocks = new object[] { new { type = "paragraph", text = "More" } },
        });

        Assert.Contains("finished PDF file", result, StringComparison.Ordinal);
        Assert.Contains("edit_pdf_pages", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewPdf_WithoutFigureEndpoint_FallsBackToText()
    {
        using var host = new PdfToolTestHost(canShowFigures: false);
        var bytes = await PdfToolFixtures.SimplePdfAsync("Fallback text works");
        await host.UploadAsync("notes.pdf", bytes);

        var preview = await host.InvokeAsync(new PreviewPdfTool(), new { });

        Assert.Contains("cannot show pictures", preview, StringComparison.Ordinal);
        Assert.Contains("Fallback text works", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("[fig:", preview, StringComparison.Ordinal);
    }
}
