using System.Text;
using System.Xml.Linq;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using UglyToad.PdfPig;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

public sealed class PdfAuthoringToolsTests
{
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
        Assert.Contains("Page 1 of 1", body, StringComparison.Ordinal);
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
